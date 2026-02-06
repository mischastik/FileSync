# FileSync
Server-/ Client-based TCP/IP File Synchronization Tool

## Warning
This is a test project to assess the capabilities of AI-Coding Tools. Feel free to use or improve it but **DON'T TRUST IT with sensitive data**. Your data may not be safe in terms of access by third parties or may be lost or compromised due to bugs.

## Modern Features & Improvements
- **Async Networking**: Fully asynchronous non-blocking I/O for better performance and reliability.
- **Hostname Support**: Connect using IP addresses or domains.
- **Robust Delta-Sync**: Per-file metadata comparison with 1-second timestamp tolerance ensures reliable updates even when files are copied with old timestamps.
- **Recovery Options**: Built-in "Force Resync" (GUI) and `resync` (CLI) to recover from out-of-sync states.
- **Docker Ready**: Easy server deployment using Docker and Docker Compose.

## Usage

### Server Setup (Console or Docker)
The server maintains the "Source of Truth".

**Console:**
```powershell
./FileSync.Server.exe
```

**Docker:**
```powershell
docker-compose up -d
```
The server outputs its address, port, and **Public Key**. Clients need this key for the initial encrypted handshake.

### Client Setup (CLI)
1. **Configure:**
   ```powershell
   ./FileSync.Client.CLI.exe config --server <address> --port <port> --key "<server-public-key>" --root "<path-to-sync>"
   ```
2. **Sync:**
   ```powershell
   ./FileSync.Client.CLI.exe sync
   ```
3. **Emergency Resync (Clear local state & full sync):**
   ```powershell
   ./FileSync.Client.CLI.exe resync
   ```

### Client Setup (GUI)
The **Avalonia UI** client allows manual synchronization with visual status:
- **Synchronize**: Standard delta-sync.
- **Force Resync**: Clears local state and forces a full server-to-client comparison.
- **Unregister**: Removes client identity from the server.

## Network and Internet Safety
If you run the applications within your local network, you should be fine. You may need to allow the server application to listen on the port it is configured to use in your firewalls.

**WARNING: Using the application over the internet is RISKY.** 
It exposes the application to the internet where any third party could attempt to exploit vulnerabilities. Only do this if you are aware of the risk and know how to configure secure port forwarding.

If you use a Dynamic DNS service, you can provide the domain name to the client instead of an IP address.

## Internal Principles

### Data Transfer & Encryption
Communication uses a proprietary binary protocol over TCP/IP (default port 32111). 
- **Encryption**: RSA-based handshake and encrypted metadata exchange.
- **Async**: Built using `async/await` and `NetworkStream` async primitives.

### Synchronization Mechanism
Updates are always initiated manually by the user.

#### 1st Step: Update from Client
1. **Delta-Scan**: Client scans the root folder and compares each file's `LastWriteTime` and `Size` against its local state (`client_state.json`) using a 1-second tolerance.
2. **Transfer**: Client sends a list of new, modified, or deleted files to the server.
3. **Server Processing**: Server updates its file storage and database. Conflict resolution follows "Last Write Wins" (UTC timestamps).

#### 2nd Step: Update of the Client
1. **Server Metadata List**: The server sends a full list of its current metadata to the client.
2. **Client Comparison**: The client identifies what it needs from the server (deletions or downloads).
3. **Reconciliation**: Client downloads new/modified files and updates its local state only upon successful completion.

### Registration & File Handling
- **IDs**: Each client generates a unique 64-bit ID.
- **Deletions**: The server tracks deletion events to propagate them to all clients before pruning records.
- **Unregistration**: Users can unregister to clear their identity from the server's known client list.

## Configuration Files

The application uses JSON files for configuration and state persistence. These files are typically found in the executable's directory or a dedicated `Config` folder.

### Server: `server_config.json`
Located in the server executable directory. It is generated automatically on first run.

- **`RootPath`**: The directory on the server where all synchronized files are stored.
- **`Port`**: The TCP port the server listens on (default: `32111`).
- **`PublicKey`**: The server's RSA public key (shared with clients).
- **`PrivateKey`**: The server's RSA private key (keep this secret).

**Sample:**
```json
{
  "RootPath": "Storage",
  "Port": 32111,
  "PublicKey": "MIIBCgKCAQ...",
  "PrivateKey": "MIIEpAIBAA..."
}
```

### Client: `config.json`
Located in the client executable directory. Created via the `config` command or manually.

- **`ServerAddress`**: Hostname or IP of the FileSync server.
- **`ServerPort`**: Port of the FileSync server.
- **`RootPath`**: Local directory to be synchronized.
- **`ClientId`**: Unique UUID for this client instance.
- **`PublicKey` / `PrivateKey`**: Client's RSA key pair for secure handshake.
- **`ServerPublicKey`**: The server's public key (must be entered manually for the first connection).

**Sample:**
```json
{
  "ServerAddress": "sync.example.com",
  "ServerPort": 32111,
  "RootPath": "C:\\Users\\User\\Documents\\Sync",
  "ClientId": "bd664ad8-df4c-4d49-b755-4816da0ba18c",
  "ServerPublicKey": "MIIBCgKCAQ..."
}
```

### Client State: `client_state.json`
This file tracks the synchronization status and is critical for delta-sync functionality. **Do not edit manually** unless troubleshooting.

- **`KnownFiles`**: A dictionary tracking every file's `RelativePath`, `LastWriteTimeUtc`, and `Size`.
- **`LastSync`**: Timestamp of the last successful full synchronization.

## Technologies
- **Runtime**: .NET 9
- **GUI**: Avalonia UI
- **Database**: LiteDB (Server-side metadata)
- **Networking**: System.Net.Sockets (Async)

