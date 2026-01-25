using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileSync.Common.Models;

namespace FileSync.Common.Client.Data;

public class LocalState
{
    private readonly string _statePath;
    public Dictionary<string, FileMetadata> KnownFiles { get; set; } = new();
    public DateTime? LastSync { get; set; }

    public LocalState()
    {
        _statePath = "client_state.json";
    }

    public LocalState(string rootPath)
    {
        _statePath = "client_state.json";
        Load();
    }

    public void Load()
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = File.ReadAllText(_statePath);
                // Try to deserialize as LocalState object first
                // Note: Older version might be just a Dictionary, so we might need a fallback if we want to support migration,
                // but for this dev stage, we can just switch.
                // However, deserializing a dictionary JSON into a LocalState object might fail or result in null props.
                // Let's rely on the fact that the JSON structure changed.

                var state = JsonSerializer.Deserialize<LocalState>(json);
                if (state != null)
                {
                    KnownFiles = state.KnownFiles ?? new();
                    LastSync = state.LastSync;
                }
                Console.WriteLine($"[LocalState] Loaded {KnownFiles.Count} files from {_statePath}. LastSync: {LastSync}");
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
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statePath, json);
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
