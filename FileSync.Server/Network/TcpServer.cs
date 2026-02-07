using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FileSync.Common.Security;
using FileSync.Server.Config;
using FileSync.Server.Data;
using FileSync.Common.Protocol;

namespace FileSync.Server.Network;

public class TcpServer
{
    private readonly ServerConfig _config;
    private readonly MetadataDb _db;
    private TcpListener _listener;
    private bool _isRunning;

    public TcpServer(ServerConfig config, MetadataDb db)
    {
        _config = config;
        _db = db;
        _listener = new TcpListener(IPAddress.Any, config.Port);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        _isRunning = true;
        int pid = Environment.ProcessId;
        int tid = Environment.CurrentManagedThreadId;
        Console.WriteLine($"[{pid}:{tid}] Server started on port {_config.Port}");

        while (_isRunning)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClient(client));
            }
            catch (Exception ex) when (_isRunning)
            {
                Console.WriteLine($"[{pid}:{tid}] Error accepting client: {ex.Message}");
            }
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        var remoteEp = client.Client.RemoteEndPoint;
        int pid = Environment.ProcessId;
        int tid = Environment.CurrentManagedThreadId;
        Console.WriteLine($"[{pid}:{tid}][Server] Client connected: {remoteEp}");

        // Configure Socket
        client.NoDelay = true;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        using (client)
        using (var netStream = client.GetStream())
        {
            try
            {
                // 1. Handshake or Registration actions
                // Expect Handshake or Unregister Packet
                Packet pkg;
                try
                {
                    pkg = await Packet.ReadFromStreamAsync(netStream);
                }
                catch (EndOfStreamException)
                {
                    // Ignore immediate disconnects (often port probes)
                    return;
                }

                Console.WriteLine($"[Server][{remoteEp}] Received Packet Type: {pkg.Type}, Payload Length: {pkg.Payload.Length}");

                if (pkg.Type == MessageType.Unregister)
                {
                    var idToUnreg = System.Text.Encoding.UTF8.GetString(pkg.Payload);
                    _db.UnregisterClient(idToUnreg);
                    Console.WriteLine($"[Server][{remoteEp}] Unregistered client: {idToUnreg}");
                    return;
                }

                if (pkg.Type != MessageType.Handshake)
                {
                    Console.WriteLine($"[Server][{remoteEp}] Unexpected packet type: {pkg.Type}. Expected Handshake.");
                    return;
                }

                // Handshake JSON: { "ClientId": "...", "PublicKey": "..." }
                string jsonString = System.Text.Encoding.UTF8.GetString(pkg.Payload);
                var handshakePayload = JsonSerializer.Deserialize<JsonElement>(pkg.Payload);
                var clientId = handshakePayload.GetProperty("ClientId").GetString();
                var publicKey = handshakePayload.GetProperty("PublicKey").GetString();

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(publicKey))
                {
                    Console.WriteLine($"[Server][{remoteEp}] Invalid handshake payload.");
                    return;
                }

                Console.WriteLine($"[Server][{remoteEp}] Handshake from {clientId}");

                // 1.1 Register/Validate Client
                var existing = _db.GetClient(clientId);
                if (existing == null)
                {
                    Console.WriteLine($"[Server][{remoteEp}] New client detected. Registering: {clientId}");
                    _db.RegisterClient(clientId, publicKey);
                }
                else
                {
                    // Existing client - Validate Public Key
                    if (existing.Value.PublicKey != publicKey)
                    {
                        Console.WriteLine($"[Server][{remoteEp}][Security] Client {clientId} failed validation: Public Key Mismatch!");
                        var err = new Packet { Type = MessageType.Error, Payload = System.Text.Encoding.UTF8.GetBytes("Invalid Public Key") };
                        await WritePacketAsync(netStream, err, remoteEp);
                        return;
                    }
                }

                // Send Handshake ACK
                var ack = new Packet { Type = MessageType.Handshake, Payload = System.Text.Encoding.UTF8.GetBytes("OK") };
                await WritePacketAsync(netStream, ack, remoteEp);

                // 2. Wait for Client's List (Step 1)
                Console.WriteLine($"[Server][{remoteEp}] Waiting for Client List Request...");
                var clientListPkg = await Packet.ReadFromStreamAsync(netStream);
                if (clientListPkg.Type != MessageType.ListRequest)
                {
                    Console.WriteLine($"[Server][{remoteEp}] Expected ListRequest, got {clientListPkg.Type}");
                    return;
                }

                Console.WriteLine($"[Server][{remoteEp}] Received ListRequest. Processing...");
                var clientFiles = JsonSerializer.Deserialize<List<Common.Models.FileMetadata>>(clientListPkg.Payload) ?? new List<Common.Models.FileMetadata>();

                Console.WriteLine($"[Server][{remoteEp}] Scanning server files...");
                var serverFiles = ScanServerFiles();
                Console.WriteLine($"[Server][{remoteEp}] Scan complete. Comparing lists...");

                // Process Client's List
                foreach (var clientFile in clientFiles)
                {
                    // Logic: If client file is newer or new, request it.
                    var serverFile = serverFiles.FirstOrDefault(f => f.RelativePath == clientFile.RelativePath);

                    if (clientFile.IsDeleted)
                    {
                        // Client says it's deleted. 
                        // Update DB to mark as deleted.
                        if (serverFile == null || !serverFile.IsDeleted || clientFile.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                        {
                            var fullPath = Path.Combine(_config.RootPath, clientFile.RelativePath);
                            if (File.Exists(fullPath))
                            {
                                Console.WriteLine($"[Server][{remoteEp}] Deleting {clientFile.RelativePath} (Sync from Client)");
                                File.Delete(fullPath);
                            }

                            // Update DB
                            clientFile.IsDeleted = true; // Ensure flag
                            _db.UpdateFile(clientFile);
                            // Update in-memory list so we send correct list back
                            if (serverFile != null) serverFile.IsDeleted = true;
                        }
                        continue;
                    }

                    bool fetchFromClient = false;

                    if (serverFile == null)
                    {
                        fetchFromClient = true; // New file
                        Console.WriteLine($"[Server][{remoteEp}][Step1] New file from client: {clientFile.RelativePath}");
                    }
                    else if (serverFile.IsDeleted)
                    {
                        // Server has it marked as deleted.
                        // Check timestamps.
                        if (clientFile.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                        {
                            // Client file is NEWER (re-created after deletion), so we fetch it.
                            fetchFromClient = true;
                            Console.WriteLine($"[Server][{remoteEp}][Step1] Client file {clientFile.RelativePath} is newer ({clientFile.LastWriteTimeUtc}) than Server Tombstone ({serverFile.LastWriteTimeUtc}). Restoring.");
                        }
                        else
                        {
                            // Server deletion is newer (or same). Client has out-of-date file.
                            Console.WriteLine($"[Server][{remoteEp}][Step1] Ignoring {clientFile.RelativePath}: Server Tombstone ({serverFile.LastWriteTimeUtc}) >= Client ({clientFile.LastWriteTimeUtc})");
                        }
                    }
                    else if (clientFile.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                    {
                        fetchFromClient = true; // Newer on client
                        Console.WriteLine($"[Server][{remoteEp}][Step1] Update from client: {clientFile.RelativePath} (Client: {clientFile.LastWriteTimeUtc} > Server: {serverFile.LastWriteTimeUtc})");
                    }
                    else
                    {
                        Console.WriteLine($"[Server][{remoteEp}][Step1] Ignoring {clientFile.RelativePath}: Server ({serverFile.LastWriteTimeUtc}) >= Client ({clientFile.LastWriteTimeUtc})");
                    }

                    if (fetchFromClient)
                    {
                        Console.WriteLine($"[Server][{remoteEp}] Requesting {clientFile.RelativePath} from client...");
                        var req = new Packet
                        {
                            Type = MessageType.FileRequest,
                            Payload = System.Text.Encoding.UTF8.GetBytes(clientFile.RelativePath)
                        };
                        await WritePacketAsync(netStream, req, remoteEp);

                        var resp = await Packet.ReadFromStreamAsync(netStream);
                        if (resp.Type == MessageType.FileResponse)
                        {
                            var localPath = Path.Combine(_config.RootPath, clientFile.RelativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                            await File.WriteAllBytesAsync(localPath, resp.Payload);
                            File.SetLastWriteTimeUtc(localPath, clientFile.LastWriteTimeUtc);
                            Console.WriteLine($"[Server][{remoteEp}] Received {clientFile.RelativePath} ({resp.Payload.Length} bytes)");

                            // Update DB
                            _db.UpdateFile(clientFile);
                        }
                    }
                }

                // Step 1 Done.
                // 3. Send Server List (Step 2)
                Console.WriteLine($"[Server][{remoteEp}] Step 1 Complete. Preparing server list for Step 2...");
                serverFiles = ScanServerFiles(); // Rescan to include what we just got
                var listResp = new Packet
                {
                    Type = MessageType.ListResponse,
                    Payload = JsonSerializer.SerializeToUtf8Bytes(serverFiles)
                };
                await WritePacketAsync(netStream, listResp, remoteEp);

                // 4. Serve requested files
                Console.WriteLine($"[Server][{remoteEp}] Waiting for client file requests...");
                while (true)
                {
                    try
                    {
                        var req = await Packet.ReadFromStreamAsync(netStream);
                        if (req.Type == MessageType.FileRequest)
                        {
                            var relPath = System.Text.Encoding.UTF8.GetString(req.Payload);
                            Console.WriteLine($"[Server][{remoteEp}] Client requested {relPath}");
                            var fullPath = Path.Combine(_config.RootPath, relPath);
                            if (File.Exists(fullPath))
                            {
                                var bytes = await File.ReadAllBytesAsync(fullPath);
                                await WritePacketAsync(netStream, new Packet { Type = MessageType.FileResponse, Payload = bytes }, remoteEp);
                            }
                            else
                            {
                                await WritePacketAsync(netStream, new Packet { Type = MessageType.Error }, remoteEp);
                            }
                        }
                        else if (req.Type == MessageType.EndOfSync)
                        {
                            Console.WriteLine($"[Server][{remoteEp}] EndOfSync received.");
                            break;
                        }
                    }
                    catch (EndOfStreamException) { break; }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server][{remoteEp}] Error handling client: {ex.Message}");
                if (!(ex is EndOfStreamException))
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
    }

    private List<Common.Models.FileMetadata> ScanServerFiles()
    {
        // 1. Get files from DB (includes Tombstones)
        var dbFiles = _db.GetAllFiles();
        var dbFileDict = dbFiles.ToDictionary(f => f.RelativePath);

        // 2. Scan Disk to catch any manual changes on server (optional but good for robustness)
        // If we want strict syncing, we might rely purely on DB, but scanning disk handles server-side edits.
        if (!Directory.Exists(_config.RootPath)) Directory.CreateDirectory(_config.RootPath);

        foreach (var file in Directory.GetFiles(_config.RootPath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(_config.RootPath, file);

            if (dbFileDict.TryGetValue(relativePath, out var dbEntry))
            {
                // If disk is newer, update DB
                if (info.LastWriteTimeUtc > dbEntry.LastWriteTimeUtc)
                {
                    dbEntry.LastWriteTimeUtc = info.LastWriteTimeUtc;
                    dbEntry.Size = info.Length;
                    dbEntry.IsDeleted = false;
                    _db.UpdateFile(dbEntry);
                }
            }
            else
            {
                // New file on server side (manual copy?), add to DB
                var newEntry = new Common.Models.FileMetadata
                {
                    RelativePath = relativePath,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    CreationTimeUtc = info.CreationTimeUtc,
                    Size = info.Length,
                    IsDeleted = false
                };
                _db.UpdateFile(newEntry);
                dbFiles.Add(newEntry);
                dbFileDict[relativePath] = newEntry; // Add to dict for next checks
            }
        }

        // 3. We return the merged list. 
        // Note: files in DB but NOT on disk should be marked IsDeleted if they aren't already?
        // Actually, if file is missing from disk but DB says IsDeleted=false, it means someone deleted it manually on server.
        // We should detect that too.

        var currentDiskFiles = Directory.GetFiles(_config.RootPath, "*", SearchOption.AllDirectories)
                                        .Select(f => Path.GetRelativePath(_config.RootPath, f))
                                        .ToHashSet();

        foreach (var dbFile in dbFiles)
        {
            if (!dbFile.IsDeleted && !currentDiskFiles.Contains(dbFile.RelativePath))
            {
                // Deleted manually on server
                dbFile.IsDeleted = true;
                dbFile.LastWriteTimeUtc = DateTime.UtcNow;
                _db.UpdateFile(dbFile);
                Console.WriteLine($"[ScanServerFiles] Detected manual deletion of {dbFile.RelativePath} on Server.");
            }
        }

        return dbFiles;
    }

    private async Task WritePacketAsync(NetworkStream stream, Common.Protocol.Packet packet, EndPoint? remoteEp)
    {
        int pid = Environment.ProcessId;
        int tid = Environment.CurrentManagedThreadId;
        Console.WriteLine($"[{pid}:{tid}][Server][{remoteEp}] Sending Packet Type: {packet.Type}, Payload Length: {packet.Payload.Length}");
        await packet.WriteToStreamAsync(stream);
    }
}
