using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSync.Common.Client.Config;
using FileSync.Common.Client.Services;
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
        bool newlyCreated = false;
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
        }
        else
        {
            _config = new ClientConfig();
            newlyCreated = true;
        }

        // Generate Keys if missing
        if (string.IsNullOrEmpty(_config.PublicKey))
        {
            var keys = FileSync.Common.Security.CryptoHelper.GenerateKeys();
            _config.PublicKey = keys.PublicKey;
            _config.PrivateKey = keys.PrivateKey;
            newlyCreated = true;
        }

        if (newlyCreated)
        {
            // Initial save to persist keys and ID
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
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

    private async void OnUnregisterClick(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        StatusText.Text = "Unregistering...";
        try
        {
            var service = new SyncService(_config);
            await service.UnregisterAsync();
            StatusText.Text = "Successfully unregistered from server.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Unregister Error: {ex.Message}";
        }
    }

    private void RefreshFileList()
    {
        var service = new SyncService(_config);
        var files = service.GetLocalFiles();
        var displayList = new System.Collections.Generic.List<string>();
        foreach (var f in files)
        {
            if (!f.IsDeleted)
            {
                displayList.Add($"{f.RelativePath} ({f.Size} bytes)");
            }
        }
        FilesList.ItemsSource = displayList;
    }
}