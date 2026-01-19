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
                KnownFiles = JsonSerializer.Deserialize<Dictionary<string, FileMetadata>>(json) ?? new();
                Console.WriteLine($"[LocalState] Loaded {KnownFiles.Count} files from {_statePath}");
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[LocalState] Error loading state: {ex.Message}");
                KnownFiles = new(); 
            }
        }
        else
        {
             Console.WriteLine($"[LocalState] State file not found at {_statePath}");
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var json = JsonSerializer.Serialize(KnownFiles);
            File.WriteAllText(_statePath, json);
            // Console.WriteLine($"[LocalState] Saved state to {_statePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalState] Error saving state: {ex.Message}");
        }
    }
    
    public void UpdateFile(FileMetadata file)
    {
        KnownFiles[file.RelativePath] = file;
        Save();
    }
}
