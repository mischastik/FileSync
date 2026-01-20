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
                HandleUnregister();
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
        Console.WriteLine("  config --server <ip> --port <port> [--key <pubkey>] [--root <path>]");
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
                        config.ServerIp = val;
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
        Console.WriteLine($"Server: {config.ServerIp}:{config.ServerPort}");
        Console.WriteLine($"Root: {config.RootPath}");
    }

    private static async Task HandleSync()
    {
        var config = LoadConfig();
        Console.WriteLine($"Starting Sync with {config.ServerIp}:{config.ServerPort}...");
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

    private static void HandleUnregister()
    {
        // TODO: Implement unregister logic if protocol supports it
        Console.WriteLine("Unregister not implemented.");
    }

    private static ClientConfig LoadConfig()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
            }
            catch
            {
                return new ClientConfig();
            }
        }
        return new ClientConfig();
    }

    private static void SaveConfig(ClientConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
