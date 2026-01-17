using System;

namespace FileSync.Server.Config;

public class ServerConfig
{
    public string RootPath { get; set; } = "Storage";
    public int Port { get; set; } = 32111;
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
}
