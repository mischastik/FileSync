# FileSync
Server-/ Client-based TCP/IP File Synchronization Tool

## Warning
This is a test project to assess the capabilities of AI-Coding Tools. Feel free to use or improve it but DON'T TRUST IT with sensible data. Your data may not be safe in terms of access by third parites or may be lost or compromised due to bugs.

## Usage
The server and client applications are console applications. The server application can be started without parameters. The client application requires the server IP and port as parameters. There is also a GUI-based client.

A Docker configuration for the server is available to run it within a container.
Use the command
 docker-compose up -d
to start the server.
The server outputs its public IP address and port to the console. This IP address and port need to be provided to the client application.

## Abstract
FileSync is a Server- / Client-based file and folder synchronization tool written in C#. It is compatible with Linux and Windows.
The server holds the most recent versions of all files and takes care of synchronizing changes over all clients.
Each client has a root folder configured for which all contents, files and folders, are synchronized, regardless of their contents and formats.
The server maintains an internal database to keep track of known clients and other relevant metadata.

## Data Transfer 
The communication between server and clients is a proprietary, binary protocoll over TCP/IP. The default port is 32111 but other ports can be configured, however, one server only communicates over a single port. A client may only communicate with one server. If another server should be used it needs to unregister from the original server.

## Encryption
The communication between server and client is encrypted. The public key of the server is manually entered by the user of each client prior to the first connection to the server.

## Synchronization Mechanism

Updates are always initiated manually by the user of a client. 

### 1st Step: Update from Client
- If there are no changes since the last synchronization, this step is skipped and the client proceeds with the second step.
- Clients initiates synchronization 
- It checks all the file modification and creation dates and compares them with the state of the last update to determine any changes.
- It creates a list of all files that were either newly created, deleted or changed.
- The list contains the file paths (relative to the root folder) and all file metadata.
- It opens a connection to the server and sends the list. 
- Updates are processed by the server on a first come first served basis, i.e. while an update is processed, other incoming updates are queued.
- The server checks if a files modification date is newer or the file was newly created and deleted.
- For all files which have a newer state on the client, it requests the contents of the file from the client and updates it's state.
### 2nd Step: Update of the Client
- When the new server has processed an update from the client, it in turn updates the client.
- It sends a full list of file metadata to the client including file modification, deletion and creation dates.
- The client compares the list with its state
  - It carries out all new deletions
  - It requests all contents of new or modified files from the server and updates its folder and state.
 
### Registration of New Clients
Each client gives itself a unique, random ID with a length 64 bits. The server keeps a list of all known clients and their public encryption keys. If a new client first connects to the server, it transfers its public key to the server.

### File Deletion Handling
The server also keeps track of file deletion events and their transfer to the clients. If a deletion was synchronized to all the clients, the server removes the record of the deletion event in order to avoid excessive growth of the database.

### Unregistration of Clients
The user of a client can unregister it from the server. In case of an unregistration, the server updates its list of known clients.

## Configuration
Configurations are held in JSON files and the server's resp. clients' configuration directories.
The configuration parameters are:

### Client
- Root folder
- Server IP
- Server port
- Server public key
- Client public key
- Client private key

### Server
- Root folder
- Port
- Server public key
- Server private key
 
## Applications

### Common Code
All code that is shared between the server and client applications is contained in a .NET DLL that is references by both applications.

### Client
The client is an application with a graphical user interface that displays the synchoronized folder structure and highlights modified files and folders since the last synchronization (by using a bold font and different color).
It also displays the server IP and port, and allows to unregister the client. When the client is first started or first started after an unregistration, the user is asked to provide the server IP and port (default port 32111). If no client encrytion key pair is set in the configuration, it creates one and writes it the to configuration.
In case of an unregistration event, the user is asked if contents of the synced folder should be deleted or moved to a provided location. The original folder should be empty if a client is not registered.
The UI contains a "Synchronize" button to manually initiate a synchronization with the server.

### Server
The server is a console application without user interface. If no private or public key are set after startup, it creates a key pair and writes it the the JSON configuration file.

