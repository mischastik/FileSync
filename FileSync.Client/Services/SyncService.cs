using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json;
using FileSync.Common.Models;
using FileSync.Common.Protocol;
using FileSync.Client.Config;
using FileSync.Client.Data;

namespace FileSync.Client.Services;

public class SyncService
{
    private readonly ClientConfig _config;
    private readonly Data.LocalState _localState;

    public SyncService(ClientConfig config)
    {
        _config = config;
        _localState = new Data.LocalState(_config.RootPath);
        Console.WriteLine($"[SyncService] Initialized with RootPath: {Path.GetFullPath(_config.RootPath)}");
    }

    public List<FileMetadata> GetLocalFiles(bool deltaOnly = false)
    {
        var files = new List<FileMetadata>();
        if (!Directory.Exists(_config.RootPath))
            Directory.CreateDirectory(_config.RootPath);

        // 1. Scan current files
        var currentFiles = new HashSet<string>();
        foreach (var file in Directory.GetFiles(_config.RootPath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(_config.RootPath, file);
            currentFiles.Add(relativePath);

            var meta = new FileMetadata
            {
                RelativePath = relativePath,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                CreationTimeUtc = info.CreationTimeUtc,
                Size = info.Length,
                IsDeleted = false
            };

            if (deltaOnly && _localState.LastSync.HasValue)
            {
                if (meta.LastWriteTimeUtc <= _localState.LastSync.Value)
                {
                    // Debug Log
                    Console.WriteLine($"[Delta-Skip] {meta.RelativePath} ({meta.LastWriteTimeUtc}) <= LastSync ({_localState.LastSync.Value})");
                    continue; // Skip unchanged
                }
                else
                {
                    Console.WriteLine($"[Delta-Include] {meta.RelativePath} ({meta.LastWriteTimeUtc}) > LastSync ({_localState.LastSync.Value})");
                }
            }
            else if (deltaOnly && !_localState.LastSync.HasValue)
            {
                Console.WriteLine($"[Delta-All] {meta.RelativePath} (First Sync)");
            }

            // Update known state
            _localState.UpdateFile(meta);
            files.Add(meta);
        }

        Console.WriteLine($"[Scan] Current on disk: {currentFiles.Count}, Known in State: {_localState.KnownFiles.Count}");

        // 2. Check for deletions (Files in KnownFiles but not on disk)
        foreach (var known in _localState.KnownFiles.Values)
        {
            if (!currentFiles.Contains(known.RelativePath))
            {
                // File is missing.
                if (!known.IsDeleted)
                {
                    // Newly detected deletion
                    known.IsDeleted = true;
                    known.LastWriteTimeUtc = DateTime.UtcNow; // Mark deletion time
                    _localState.UpdateFile(known);
                    files.Add(known);
                    Console.WriteLine($"[Scan] Marked {known.RelativePath} as Deleted.");
                }
                else
                {
                    // Already marked deleted.
                    if (deltaOnly)
                    {
                        // Only send if it was deleted AFTER LastSync
                        if (!_localState.LastSync.HasValue || known.LastWriteTimeUtc > _localState.LastSync.Value)
                        {
                            files.Add(known);
                        }
                    }
                    else
                    {
                        // Return all deletions for UI if needed (though UI filters them out)
                        files.Add(known);
                    }
                }
            }
        }

        if (deltaOnly) Console.WriteLine($"[GetChanges] Found {files.Count} changes to sync.");
        return files;
    }

    public async Task SyncAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_config.ServerIp, _config.ServerPort);
            using var stream = client.GetStream();

            // --- 0. Handshake ---
            var handshake = new Packet
            {
                Type = MessageType.Handshake,
                Payload = System.Text.Encoding.UTF8.GetBytes(_config.ClientId)
            };
            WritePacket(stream, handshake);

            // Wait for Handshake ACK? (Simplification: Assuming server is ready if connected)
            // Implementation: Server should probably send an Ack or we proceed. 
            // Let's read one packet to be sure server accepted us.
            var response = ReadPacket(stream);
            if (response.Type != MessageType.ListResponse && response.Type != MessageType.Handshake)
            {
                // Expected Handshake ACK or we can proceed.
                // If Server sends ListResponse immediately (as per my previous server stub), we handle it.
                // But wait, step 1 is "Update from Client" (Client sends list).
                // So Server should just Ack the handshake.
            }

            // --- 1. Update from Client ---
            var localChanges = GetLocalFiles(true); // Delta Sync

            var listPacket = new Packet
            {
                Type = MessageType.ListRequest, // Client sending its list
                Payload = JsonSerializer.SerializeToUtf8Bytes(localChanges)
            };
            WritePacket(stream, listPacket);

            // Server processes list and requests files. 
            // We enter a loop to handle server requests until Server says "Step 1 Done" or starts Step 2.
            bool clientUpdateDone = false;
            while (!clientUpdateDone)
            {
                var pkg = ReadPacket(stream);
                switch (pkg.Type)
                {
                    case MessageType.FileRequest:
                        // Uplad File
                        var relPath = System.Text.Encoding.UTF8.GetString(pkg.Payload);
                        var fullPath = Path.Combine(_config.RootPath, relPath);
                        if (File.Exists(fullPath))
                        {
                            var bytes = await File.ReadAllBytesAsync(fullPath);
                            var fileResp = new Packet { Type = MessageType.FileResponse, Payload = bytes };
                            WritePacket(stream, fileResp);
                        }
                        else
                        {
                            // File not found (deleted?) - Send empty for now or Error
                            WritePacket(stream, new Packet { Type = MessageType.Error });
                        }
                        break;
                    case MessageType.ListResponse:
                        // Server is done with Step 1 and is now sending US its list (Step 2 starts)
                        // This packet contains Server's file list.
                        HandleServerList(pkg.Payload, stream);
                        clientUpdateDone = true;

                        // Sync Successful (at least Step 1 and reception of Step 2)
                        // If separate try/catch for handleServerList, we might be more granular.
                        // But here, we consider sync round complete.
                        _localState.LastSync = DateTime.UtcNow;

                        // Prune synced deletions
                        var toRemove = _localState.KnownFiles.Values
                            .Where(f => f.IsDeleted && f.LastWriteTimeUtc <= _localState.LastSync)
                            .Select(f => f.RelativePath)
                            .ToList();

                        foreach (var key in toRemove)
                        {
                            _localState.KnownFiles.Remove(key);
                        }
                        Console.WriteLine($"[Sync] Pruned {toRemove.Count} deleted files from state.");

                        _localState.Save();
                        Console.WriteLine($"[Sync] Sync Complete. LastSync Updated to {_localState.LastSync}");
                        break;
                    case MessageType.EndOfSync:
                        clientUpdateDone = true;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Sync failed: {ex.Message}");
        }
    }

    private void HandleServerList(byte[] payload, NetworkStream stream)
    {
        var serverFiles = JsonSerializer.Deserialize<List<FileMetadata>>(payload);
        if (serverFiles == null) return;

        foreach (var serverFile in serverFiles)
        {
            var localPath = Path.Combine(_config.RootPath, serverFile.RelativePath);

            if (serverFile.IsDeleted)
            {
                // Server says delete this file
                // Check if we have a Newer local change?
                if (File.Exists(localPath))
                {
                    var localInfo = new FileInfo(localPath);
                    if (localInfo.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                    {
                        Console.WriteLine($"[Sync] Conflict: Local file is NEWER ({localInfo.LastWriteTimeUtc}) than Server Deletion ({serverFile.LastWriteTimeUtc}). Keeping Local.");
                        continue;
                    }

                    Console.WriteLine($"[Sync] Deleting local file {serverFile.RelativePath} (Sync from Server)");
                    File.Delete(localPath);
                }

                // Server says deleted. We accepted it (file is gone from disk).
                // We should stop tracking it in LocalState so we don't report it as missing again, 
                // and so we don't constantly Prune it.
                if (_localState.KnownFiles.ContainsKey(serverFile.RelativePath))
                {
                    _localState.KnownFiles.Remove(serverFile.RelativePath);
                    _localState.Save();
                }
                continue;
            }

            bool needsUpdate = true;

            if (File.Exists(localPath))
            {
                var localInfo = new FileInfo(localPath);
                Console.WriteLine($"[Sync] Checking {serverFile.RelativePath}: Local({localInfo.LastWriteTimeUtc} Kind={localInfo.LastWriteTimeUtc.Kind}) vs Server({serverFile.LastWriteTimeUtc} Kind={serverFile.LastWriteTimeUtc.Kind})");

                // Simple comparison: If server is newer
                if (localInfo.LastWriteTimeUtc >= serverFile.LastWriteTimeUtc)
                {
                    needsUpdate = false;
                }
            }

            if (needsUpdate)
            {
                Console.WriteLine($"[Sync] Updating {serverFile.RelativePath} from Server.");
                // Request File
                var req = new Packet
                {
                    Type = MessageType.FileRequest,
                    Payload = System.Text.Encoding.UTF8.GetBytes(serverFile.RelativePath)
                };
                WritePacket(stream, req);

                // Read Content
                var resp = ReadPacket(stream);
                if (resp.Type == MessageType.FileResponse)
                {
                    // Ensure Dir
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    File.WriteAllBytes(localPath, resp.Payload);
                    File.SetLastWriteTimeUtc(localPath, serverFile.LastWriteTimeUtc); // Sync timestamps

                    // Update State
                    serverFile.IsDeleted = false; // Just to be sure
                    _localState.UpdateFile(serverFile);
                }
            }
        }
    }

    private void WritePacket(NetworkStream stream, Packet packet)
    {
        var data = packet.Serialize();
        stream.Write(data);
    }

    private Packet ReadPacket(NetworkStream stream)
    {
        return Packet.ReadFromStream(stream);
    }
}
