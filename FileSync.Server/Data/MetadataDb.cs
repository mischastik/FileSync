using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FileSync.Server.Data;

public class MetadataDb
{
    private readonly string _connectionString;

    public MetadataDb(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Clients Table
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Clients (
                Id TEXT PRIMARY KEY,
                PublicKey TEXT,
                LastSync TEXT
            );
        ";
        cmd.ExecuteNonQuery();

        // Files Table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Files (
                RelativePath TEXT PRIMARY KEY,
                LastWriteTimeUtc TEXT,
                CreationTimeUtc TEXT,
                IsDeleted INTEGER,
                Size INTEGER
            );
        ";
        cmd.ExecuteNonQuery();
    }
    
    // Methods to AddClient, GetClient, UpdateFile, GetAllFiles, etc. will go here
    public void RegisterClient(string clientId, string publicKey)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Clients (Id, PublicKey, LastSync) VALUES ($id, $key, $sync)";
        cmd.Parameters.AddWithValue("$id", clientId);
        cmd.Parameters.AddWithValue("$key", publicKey);
        cmd.Parameters.AddWithValue("$sync", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}
