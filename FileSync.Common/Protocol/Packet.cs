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

    // Helper to read directly from a stream WITHOUT internal buffering (avoids data loss on consecutive reads)
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
