using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FileSync.Common.Protocol;

public enum MessageType
{
    Handshake = 1,
    ListRequest = 2,
    ListResponse = 3,
    FileRequest = 4,
    FileResponse = 5,
    EndOfSync = 6,
    Unregister = 7,
    Error = 255
}

public class Packet
{
    public MessageType Type { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((int)Type);
        writer.Write(Payload.Length);
        writer.Write(Payload);
        return ms.ToArray();
    }

    public static Packet Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        var type = (MessageType)reader.ReadInt32();
        var length = reader.ReadInt32();
        var payload = reader.ReadBytes(length);
        return new Packet { Type = type, Payload = payload };
    }

    // Async version
    public static async Task<Packet> ReadFromStreamAsync(Stream stream, System.Threading.CancellationToken ct = default)
    {
        // Read Type (4 bytes) + Length (4 bytes) = 8 bytes header
        byte[] header = new byte[8];
        int offset = 0;
        while (offset < 8)
        {
            int read = await stream.ReadAsync(header.AsMemory(offset, 8 - offset), ct);
            if (read <= 0) throw new EndOfStreamException("Connection closed by remote party.");
            offset += read;
        }

        int typeInt = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);

        // Sanity check: 100MB max
        if (length < 0 || length > 100 * 1024 * 1024)
            throw new InvalidDataException($"Invalid packet length: {length}");

        byte[] payload = new byte[length];
        offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(payload.AsMemory(offset, length - offset), ct);
            if (read <= 0) throw new EndOfStreamException("Data truncated.");
            offset += read;
        }

        return new Packet { Type = (MessageType)typeInt, Payload = payload };
    }

    public async Task WriteToStreamAsync(Stream stream, System.Threading.CancellationToken ct = default)
    {
        var data = Serialize();
        await stream.WriteAsync(data.AsMemory(), ct);
        await stream.FlushAsync(ct);
    }

    // Keep sync ReadFromStream for now if needed, but we should transition.
    public static Packet ReadFromStream(Stream stream)
    {
        // Read Type (4 bytes)
        byte[] header = new byte[8];
        int offset = 0;
        while (offset < 8)
        {
            int read = stream.Read(header, offset, 8 - offset);
            if (read <= 0) throw new EndOfStreamException("Connection closed by remote party.");
            offset += read;
        }

        int typeInt = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);

        if (length < 0 || length > 100 * 1024 * 1024)
            throw new InvalidDataException($"Invalid packet length: {length}");

        byte[] payload = new byte[length];
        offset = 0;
        while (offset < length)
        {
            int read = stream.Read(payload, offset, length - offset);
            if (read <= 0) throw new EndOfStreamException("Data truncated.");
            offset += read;
        }

        return new Packet { Type = (MessageType)typeInt, Payload = payload };
    }
}
