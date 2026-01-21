using System;

namespace FileSync.Common.Client.Config;


public class ClientConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 32111;
    public string RootPath { get; set; } = "ClientFiles";
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string ServerPublicKey { get; set; } = string.Empty; // Manually entered
}
