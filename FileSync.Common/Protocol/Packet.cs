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
    
    // Helper to read directly from a stream
    public static Packet ReadFromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var typeInt = reader.ReadInt32();
        var length = reader.ReadInt32();
        var payload = reader.ReadBytes(length);
        return new Packet { Type = (MessageType)typeInt, Payload = payload };
    }
}
