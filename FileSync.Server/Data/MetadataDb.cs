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
    public (string PublicKey, DateTime LastSync)? GetClient(string clientId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT PublicKey, LastSync FROM Clients WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", clientId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return (
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind)
            );
        }
        return null;
    }

    public void UnregisterClient(string clientId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Clients WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", clientId);
        cmd.ExecuteNonQuery();
    }

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

    public void UpdateFile(Common.Models.FileMetadata file)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Files (RelativePath, LastWriteTimeUtc, CreationTimeUtc, IsDeleted, Size)
            VALUES ($path, $lastWrite, $creation, $deleted, $size)";

        cmd.Parameters.AddWithValue("$path", file.RelativePath);
        cmd.Parameters.AddWithValue("$lastWrite", file.LastWriteTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$creation", file.CreationTimeUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$deleted", file.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", file.Size);
        cmd.ExecuteNonQuery();
    }

    public List<Common.Models.FileMetadata> GetAllFiles()
    {
        var files = new List<Common.Models.FileMetadata>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT RelativePath, LastWriteTimeUtc, CreationTimeUtc, IsDeleted, Size FROM Files";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new Common.Models.FileMetadata
            {
                RelativePath = reader.GetString(0),
                LastWriteTimeUtc = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                CreationTimeUtc = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                IsDeleted = reader.GetInt32(3) == 1,
                Size = reader.GetInt64(4)
            });
        }
        return files;
    }
}
