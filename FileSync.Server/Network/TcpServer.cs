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

    public void Start()
    {
        _listener.Start();
        _isRunning = true;
        Console.WriteLine($"Server started on port {_config.Port}");
        
        while (_isRunning)
        {
            var client = _listener.AcceptTcpClient();
            Task.Run(() => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
        using (client)
        using (var netStream = client.GetStream())
        {
            try
            {
                // 1. Handshake
                // Expect Handshake Packet
                var handshakePkg = Packet.ReadFromStream(netStream);
                if (handshakePkg.Type != MessageType.Handshake) return;

                var clientId = System.Text.Encoding.UTF8.GetString(handshakePkg.Payload);
                Console.WriteLine($"Handshake from {clientId}");
                // TODO: Register/Validate Client in DB
                
                // Send Handshake ACK
                var ack = new Packet { Type = MessageType.Handshake, Payload = System.Text.Encoding.UTF8.GetBytes("OK") };
                WritePacket(netStream, ack);

                // 2. Wait for Client's List (Step 1)
                var clientListPkg = Packet.ReadFromStream(netStream);
                if (clientListPkg.Type != MessageType.ListRequest) return;

                var clientFiles = JsonSerializer.Deserialize<List<Common.Models.FileMetadata>>(clientListPkg.Payload);
                var serverFiles = ScanServerFiles();

                // Process Client's List
                foreach (var clientFile in clientFiles)
                {
                    // Logic: If client file is newer or new, request it.
                    var serverFile = serverFiles.FirstOrDefault(f => f.RelativePath == clientFile.RelativePath);
                    
                    if (clientFile.IsDeleted)
                    {
                        // Client says it's deleted. 
                        // If we have it, delete it.
                        if (serverFile != null)
                        {
                            var fullPath = Path.Combine(_config.RootPath, clientFile.RelativePath);
                            Console.WriteLine($"Deleting {clientFile.RelativePath} (Sync from Client)");
                            if (File.Exists(fullPath)) File.Delete(fullPath);
                        }
                        continue;
                    }

                    bool fetchFromClient = false;

                    if (serverFile == null)
                    {
                        fetchFromClient = true; // New file
                    }
                    else if (clientFile.LastWriteTimeUtc > serverFile.LastWriteTimeUtc)
                    {
                        fetchFromClient = true; // Newer on client
                    }

                    if (fetchFromClient)
                    {
                        Console.WriteLine($"Requesting {clientFile.RelativePath} from client...");
                        var req = new Packet 
                        { 
                            Type = MessageType.FileRequest, 
                            Payload = System.Text.Encoding.UTF8.GetBytes(clientFile.RelativePath) 
                        };
                        WritePacket(netStream, req);

                        var resp = Packet.ReadFromStream(netStream);
                        if (resp.Type == MessageType.FileResponse)
                        {
                            var localPath = Path.Combine(_config.RootPath, clientFile.RelativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                            File.WriteAllBytes(localPath, resp.Payload);
                            File.SetLastWriteTimeUtc(localPath, clientFile.LastWriteTimeUtc);
                            Console.WriteLine($"Received {clientFile.RelativePath}");
                        }
                    }
                }

                // Step 1 Done.
                // 3. Send Server List (Step 2)
                serverFiles = ScanServerFiles(); // Rescan to include what we just got
                var listResp = new Packet 
                { 
                    Type = MessageType.ListResponse, 
                    Payload = JsonSerializer.SerializeToUtf8Bytes(serverFiles) 
                };
                WritePacket(netStream, listResp);

                // 4. Serve requested files
                while (true)
                {
                    try 
                    {
                        var req = Packet.ReadFromStream(netStream);
                        if (req.Type == MessageType.FileRequest)
                        {
                            var relPath = System.Text.Encoding.UTF8.GetString(req.Payload);
                            Console.WriteLine($"Client requested {relPath}");
                            var fullPath = Path.Combine(_config.RootPath, relPath);
                            if (File.Exists(fullPath))
                            {
                                var bytes = File.ReadAllBytes(fullPath);
                                WritePacket(netStream, new Packet { Type = MessageType.FileResponse, Payload = bytes });
                            }
                            else
                            {
                                WritePacket(netStream, new Packet { Type = MessageType.Error });
                            }
                        }
                        else if (req.Type == MessageType.EndOfSync)
                        {
                            break;
                        }
                    }
                    catch (EndOfStreamException) { break; }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    private List<Common.Models.FileMetadata> ScanServerFiles()
    {
        var files = new List<Common.Models.FileMetadata>();
        if (!Directory.Exists(_config.RootPath)) Directory.CreateDirectory(_config.RootPath);

        foreach (var file in Directory.GetFiles(_config.RootPath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(_config.RootPath, file);
            files.Add(new Common.Models.FileMetadata
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

    private void WritePacket(NetworkStream stream, Common.Protocol.Packet packet)
    {
        var data = packet.Serialize();
        stream.Write(data);
    }
}
