using System;
using System.IO;
using System.Text.Json;
using FileSync.Common.Security;
using FileSync.Server.Config;
using FileSync.Server.Data;
using FileSync.Server.Network;

namespace FileSync.Server;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("FileSync Server starting...");

        // Load or Create Config
        var configPath = Path.Combine("Config", "server_config.json");
        ServerConfig config;

        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
        }
        else
        {
            config = new ServerConfig();
            // Generate Keys if missing
            if (string.IsNullOrEmpty(config.PublicKey))
            {
                var keys = CryptoHelper.GenerateKeys();
                config.PublicKey = keys.PublicKey;
                config.PrivateKey = keys.PrivateKey;
            }
            // Ensure Config Dir
            Directory.CreateDirectory("Config");
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
        
        // Ensure Storage Dir
        Directory.CreateDirectory(config.RootPath);
        
        Console.WriteLine($"Public Key (share with clients):\n{config.PublicKey}\n");

        // Init DB
        var dbPath = Path.Combine("Data", "server.db");
        Directory.CreateDirectory("Data");
        var db = new MetadataDb(dbPath);

        // Start Server
        var server = new TcpServer(config, db);
        server.Start();
    }
}
