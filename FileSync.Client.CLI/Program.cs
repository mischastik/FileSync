using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FileSync.Common.Client.Config;
using FileSync.Common.Client.Services;

namespace FileSync.Client.CLI;

class Program
{
    private static readonly string ConfigPath = "config.json"; // Same as GUI for simplicity? Or separate? 
    // Argument: If deployed to same folder, they share config.json. Good for testing.

    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        string command = args[0].ToLower();

        switch (command)
        {
            case "config":
                HandleConfig(args);
                break;
            case "sync":
                await HandleSync();
                break;
            case "unregister":
                await HandleUnregister();
                break;
            case "help":
                PrintUsage();
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FileSync CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  config --server <address> --port <port> [--key <pubkey>] [--root <path>]");
        Console.WriteLine("  sync");
        Console.WriteLine("  unregister");
    }

    private static void HandleConfig(string[] args)
    {
        var config = LoadConfig();

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i].ToLower();
            if (i + 1 < args.Length)
            {
                string val = args[i + 1];
                switch (arg)
                {
                    case "--server":
                        config.ServerAddress = val;
                        i++;
                        break;
                    case "--port":
                        if (int.TryParse(val, out int p)) config.ServerPort = p;
                        i++;
                        break;
                    case "--key":
                        config.ServerPublicKey = val;
                        i++;
                        break;
                    case "--root":
                        config.RootPath = val;
                        i++;
                        break;
                }
            }
        }

        SaveConfig(config);
        Console.WriteLine("Configuration updated.");
        Console.WriteLine($"Server: {config.ServerAddress}:{config.ServerPort}");
        Console.WriteLine($"Root: {config.RootPath}");
    }

    private static async Task HandleSync()
    {
        var config = LoadConfig();
        Console.WriteLine($"Starting Sync with {config.ServerAddress}:{config.ServerPort}...");
        try
        {
            var service = new SyncService(config);
            await service.SyncAsync();
            Console.WriteLine("Sync completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sync failed: {ex.Message}");
        }
    }

    private static async Task HandleUnregister()
    {
        var config = LoadConfig();
        Console.WriteLine($"Unregistering from {config.ServerAddress}:{config.ServerPort}...");
        try
        {
            var service = new SyncService(config);
            await service.UnregisterAsync();
            Console.WriteLine("Unregister successful.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unregister failed: {ex.Message}");
        }
    }

    private static ClientConfig LoadConfig()
    {
        ClientConfig config;
        bool newlyCreated = false;

        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
            }
            catch
            {
                config = new ClientConfig();
                newlyCreated = true;
            }
        }
        else
        {
            config = new ClientConfig();
            newlyCreated = true;
        }

        // Generate Keys if missing
        if (string.IsNullOrEmpty(config.PublicKey))
        {
            var keys = FileSync.Common.Security.CryptoHelper.GenerateKeys();
            config.PublicKey = keys.PublicKey;
            config.PrivateKey = keys.PrivateKey;
            newlyCreated = true;
        }

        if (newlyCreated)
        {
            SaveConfig(config);
        }

        return config;
    }

    private static void SaveConfig(ClientConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
