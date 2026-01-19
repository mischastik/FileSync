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

namespace FileSync.Client.Services;

public class SyncService
{
    private readonly ClientConfig _config;

    public SyncService(ClientConfig config)
    {
        _config = config;
    }

    public List<FileMetadata> ScanLocalFiles()
    {
        var files = new List<FileMetadata>();
        if (!Directory.Exists(_config.RootPath))
            Directory.CreateDirectory(_config.RootPath);

        foreach (var file in Directory.GetFiles(_config.RootPath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(_config.RootPath, file);
            files.Add(new FileMetadata
            {
                RelativePath = relativePath,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                CreationTimeUtc = info.CreationTimeUtc,
                Size = info.Length,
                IsDeleted = false
            });
        }
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
            var localFiles = ScanLocalFiles();
            // TODO: Filter only changes using LocalState (omitted for now, sending all current files as 'current state')
            // For robust sync, we need to compare with 'LastSync' state. 
            // For this iteration, we send ALL local files and let server decide if they are new/modified.
            
            var listPacket = new Packet 
            { 
                Type = MessageType.ListRequest, // Client sending its list
                Payload = JsonSerializer.SerializeToUtf8Bytes(localFiles)
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
            bool needsUpdate = true;

            if (File.Exists(localPath))
            {
                var localInfo = new FileInfo(localPath);
                // Simple comparison: If server is newer
                if (localInfo.LastWriteTimeUtc >= serverFile.LastWriteTimeUtc)
                {
                    needsUpdate = false;
                }
            }

            if (needsUpdate)
            {
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
