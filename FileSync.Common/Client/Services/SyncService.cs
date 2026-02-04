using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json;
using FileSync.Common.Models;
using FileSync.Common.Protocol;
using FileSync.Common.Client.Config;
using FileSync.Common.Client.Data;

namespace FileSync.Common.Client.Services;

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

            bool isKnown = _localState.KnownFiles.TryGetValue(relativePath, out var existingMeta);
            bool isNew = !isKnown;

            if (deltaOnly && isKnown && existingMeta != null)
            {
                // Core Delta Logic: Skip if we have a known record and the file hasn't changed since then.
                // We use a 1-second tolerance for timestamps to handle filesystem/JSON precision differences.
                bool timestampMatch = Math.Abs((meta.LastWriteTimeUtc - existingMeta.LastWriteTimeUtc).TotalSeconds) < 1;

                if (!existingMeta.IsDeleted && timestampMatch && meta.Size == existingMeta.Size)
                {
                    // Skip unchanged version we already successfully synced
                    continue;
                }

                if (existingMeta.IsDeleted)
                    Console.WriteLine($"[Scan] {meta.RelativePath} was previously deleted. Re-syncing.");
                else
                    Console.WriteLine($"[Scan] {meta.RelativePath} has changed ({meta.LastWriteTimeUtc} vs {existingMeta.LastWriteTimeUtc}). Syncing.");
            }
            else if (deltaOnly && isNew)
            {
                Console.WriteLine($"[Scan] New file: {meta.RelativePath} ({meta.LastWriteTimeUtc})");
            }

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

    public async Task UnregisterAsync()
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(_config.ServerAddress, _config.ServerPort);
            using var stream = client.GetStream();

            var unreg = new Packet
            {
                Type = MessageType.Unregister,
                Payload = System.Text.Encoding.UTF8.GetBytes(_config.ClientId)
            };
            await WritePacketAsync(stream, unreg);
            Console.WriteLine("[SyncService] Unregister request sent.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Unregister failed: {ex.Message}");
        }
    }

    public async Task SyncAsync(bool forceFullSync = false)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            Console.WriteLine($"[SyncService] Connecting to {_config.ServerAddress}:{_config.ServerPort}...");
            await client.ConnectAsync(_config.ServerAddress, _config.ServerPort);
            using var stream = client.GetStream();

            // --- 0. Handshake ---
            var handshakeData = new { ClientId = _config.ClientId, PublicKey = _config.PublicKey };
            var handshake = new Packet
            {
                Type = MessageType.Handshake,
                Payload = JsonSerializer.SerializeToUtf8Bytes(handshakeData)
            };
            await WritePacketAsync(stream, handshake);

            // Wait for Handshake ACK
            var response = await ReadPacketAsync(stream);
            if (response.Type != MessageType.ListResponse && response.Type != MessageType.Handshake)
            {
                Console.WriteLine($"[SyncService] Unexpected response during handshake: {response.Type}");
            }
            else
            {
                Console.WriteLine($"[SyncService] Handshake successful.");
            }

            // --- 1. Update from Client ---
            Console.WriteLine($"[SyncService] Scanning local files for changes...");
            var localChanges = GetLocalFiles(forceFullSync ? false : true); // Delta Sync unless forced
            if (forceFullSync) Console.WriteLine("[SyncService] Performing FULL synchronization.");
            Console.WriteLine($"[SyncService] Sending ListRequest with {localChanges.Count} changes.");

            var listPacket = new Packet
            {
                Type = MessageType.ListRequest, // Client sending its list
                Payload = JsonSerializer.SerializeToUtf8Bytes(localChanges)
            };
            await WritePacketAsync(stream, listPacket);

            // Server processes list and requests files. 
            // We enter a loop to handle server requests until Server says "Step 1 Done" or starts Step 2.
            bool clientUpdateDone = false;
            while (!clientUpdateDone)
            {
                var pkg = await ReadPacketAsync(stream);
                switch (pkg.Type)
                {
                    case MessageType.FileRequest:
                        // Upload File
                        var relPath = System.Text.Encoding.UTF8.GetString(pkg.Payload);
                        var fullPath = Path.Combine(_config.RootPath, relPath);
                        if (File.Exists(fullPath))
                        {
                            Console.WriteLine($"[SyncService] Uploading {relPath}...");
                            var bytes = await File.ReadAllBytesAsync(fullPath);
                            var fileResp = new Packet { Type = MessageType.FileResponse, Payload = bytes };
                            await WritePacketAsync(stream, fileResp);
                        }
                        else
                        {
                            // File not found (deleted?) - Send empty for now or Error
                            await WritePacketAsync(stream, new Packet { Type = MessageType.Error });
                        }
                        break;
                    case MessageType.ListResponse:
                        // Server is done with Step 1 and is now sending US its list (Step 2 starts)
                        Console.WriteLine($"[SyncService] Step 1 Complete. Processing Server List...");
                        await HandleServerListAsync(pkg.Payload, stream);
                        clientUpdateDone = true;
                        break;
                    case MessageType.EndOfSync:
                        clientUpdateDone = true;
                        break;
                    case MessageType.Error:
                        var msg = System.Text.Encoding.UTF8.GetString(pkg.Payload);
                        throw new Exception($"Server reported error: {msg}");
                }
            }

            // Sync Successful - Persist State
            foreach (var change in localChanges)
            {
                _localState.UpdateFile(change);
            }

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
            if (toRemove.Count > 0) Console.WriteLine($"[SyncService] Pruned {toRemove.Count} deleted files from state.");

            _localState.Save();
            Console.WriteLine($"[SyncService] Sync Complete. KnownFiles: {_localState.KnownFiles.Count}, LastSync: {_localState.LastSync}");

            // Send EndOfSync to server if we haven't already
            await WritePacketAsync(stream, new Packet { Type = MessageType.EndOfSync });
        }
        catch (Exception ex)
        {
            throw new Exception($"Sync failed: {ex.Message}");
        }
    }

    private async Task HandleServerListAsync(byte[] payload, NetworkStream stream)
    {
        var serverFiles = JsonSerializer.Deserialize<List<FileMetadata>>(payload);
        if (serverFiles == null) return;

        foreach (var serverFile in serverFiles)
        {
            var localPath = Path.Combine(_config.RootPath, serverFile.RelativePath);

            if (serverFile.IsDeleted)
            {
                if (File.Exists(localPath))
                {
                    var localInfo = new FileInfo(localPath);
                    if (localInfo.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                    {
                        Console.WriteLine($"[SyncService] Conflict: Local file {serverFile.RelativePath} is NEWER than Server Deletion. Keeping Local.");
                        continue;
                    }

                    Console.WriteLine($"[SyncService] Deleting local file {serverFile.RelativePath} (Sync from Server)");
                    File.Delete(localPath);
                }

                if (_localState.KnownFiles.ContainsKey(serverFile.RelativePath))
                {
                    _localState.KnownFiles.Remove(serverFile.RelativePath);
                }
                continue;
            }

            bool needsUpdate = true;
            if (File.Exists(localPath))
            {
                var localInfo = new FileInfo(localPath);
                if (localInfo.LastWriteTimeUtc >= serverFile.LastWriteTimeUtc)
                {
                    needsUpdate = false;
                }
            }

            if (needsUpdate)
            {
                Console.WriteLine($"[SyncService] Downloading {serverFile.RelativePath} from Server...");
                var req = new Packet
                {
                    Type = MessageType.FileRequest,
                    Payload = System.Text.Encoding.UTF8.GetBytes(serverFile.RelativePath)
                };
                await WritePacketAsync(stream, req);

                var resp = await ReadPacketAsync(stream);
                if (resp.Type == MessageType.FileResponse)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await File.WriteAllBytesAsync(localPath, resp.Payload);
                    File.SetLastWriteTimeUtc(localPath, serverFile.LastWriteTimeUtc);

                    serverFile.IsDeleted = false;
                    _localState.UpdateFile(serverFile);
                    Console.WriteLine($"[SyncService] Received {serverFile.RelativePath} ({resp.Payload.Length} bytes)");
                }
            }
        }
    }

    private async Task WritePacketAsync(NetworkStream stream, Packet packet)
    {
        Console.WriteLine($"[SyncService] Sending Packet Type: {packet.Type}, Payload Length: {packet.Payload.Length}");
        await packet.WriteToStreamAsync(stream);
    }

    private async Task<Packet> ReadPacketAsync(NetworkStream stream)
    {
        var pkg = await Packet.ReadFromStreamAsync(stream);
        Console.WriteLine($"[SyncService] Received Packet Type: {pkg.Type}, Payload Length: {pkg.Payload.Length}");
        return pkg;
    }
}
