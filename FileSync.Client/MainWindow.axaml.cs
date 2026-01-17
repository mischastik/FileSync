using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSync.Client.Config;
using FileSync.Client.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace FileSync.Client;

public partial class MainWindow : Window
{
    private ClientConfig _config;
    private readonly string _configPath = "config.json";

    public MainWindow()
    {
        InitializeComponent();
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
        }
        else
        {
            _config = new ClientConfig();
        }

        ServerIpBox.Text = _config.ServerIp;
        ServerPortBox.Text = _config.ServerPort.ToString();
        ServerKeyBox.Text = _config.ServerPublicKey;
        RootPathBox.Text = _config.RootPath;
        
        RefreshFileList();
    }

    private void SaveConfig()
    {
        _config.ServerIp = ServerIpBox.Text ?? "127.0.0.1";
        if (int.TryParse(ServerPortBox.Text, out int port)) _config.ServerPort = port;
        _config.ServerPublicKey = ServerKeyBox.Text ?? "";
        _config.RootPath = RootPathBox.Text ?? "ClientFiles";

        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
        StatusText.Text = "Configuration Saved.";
    }

    private void OnSaveConfigClick(object sender, RoutedEventArgs e)
    {
        SaveConfig();
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        StatusText.Text = "Synchronizing...";
        
        try
        {
            var service = new SyncService(_config);
            await service.SyncAsync();
            StatusText.Text = "Synchronization Complete.";
            RefreshFileList();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void OnUnregisterClick(object sender, RoutedEventArgs e)
    {
        // TODO: Implement Unregister logic
        StatusText.Text = "Unregister not implemented yet.";
    }

    private void RefreshFileList()
    {
        var service = new SyncService(_config);
        var files = service.ScanLocalFiles();
        var displayList = new System.Collections.Generic.List<string>();
        foreach(var f in files)
        {
            displayList.Add($"{f.RelativePath} ({f.Size} bytes)");
        }
        FilesList.ItemsSource = displayList;
    }
}