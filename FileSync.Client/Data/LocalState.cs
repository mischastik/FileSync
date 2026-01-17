using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileSync.Common.Models;

namespace FileSync.Client.Data;

public class LocalState
{
    private readonly string _statePath;
    public Dictionary<string, FileMetadata> KnownFiles { get; set; } = new();

    public LocalState(string rootPath)
    {
        _statePath = Path.Combine(rootPath, ".syncstate"); // Store in root or config? READMe doesn't specify location, but root/.syncstate is common. Or Config dir.
        // Let's put it in the Config dir to avoid cluttering the sync folder visibly
        _statePath = "Config/client_state.json"; 
        Load();
    }

    public void Load()
    {
        if (File.Exists(_statePath))
        {
            try 
            {
                var json = File.ReadAllText(_statePath);
                KnownFiles = JsonSerializer.Deserialize<Dictionary<string, FileMetadata>>(_statePath) ?? new();
            }
            catch { KnownFiles = new(); }
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.Serialize(KnownFiles);
        File.WriteAllText(_statePath, json);
    }
    
    public void UpdateFile(FileMetadata file)
    {
        KnownFiles[file.RelativePath] = file;
        Save();
    }
}
