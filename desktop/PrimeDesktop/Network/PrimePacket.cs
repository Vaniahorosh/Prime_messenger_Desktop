using System;

using System.Buffers.Binary;

using System.IO;

using System.Text;

using PrimeDesktop.Models;

namespace PrimeDesktop.Network;

public class PrimePacket

{
    public PacketType Type { get; set; }

    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public PrimePacket(PacketType type, byte[] payload)

    {
        Type = type;

        Payload = payload ?? Array.Empty<byte>();

    }

    /// <summary>

    /// Writes packet into stream matching BluetoothConnectionManager.java:

    /// Atomic 5-byte header (1 byte Type + 4 bytes Big-Endian Length), then raw Payload bytes.

    /// </summary>

    /// <summary>
    /// Writes packet into stream asynchronously matching BluetoothConnectionManager.java:
    /// Atomic 5-byte header (1 byte Type + 4 bytes Big-Endian Length), then raw Payload bytes in 16KB chunks.
    /// </summary>
    public static async Task WriteToStreamAsync(Stream stream, PacketType type, byte[] payload, CancellationToken ct = default)
    {
        int len = payload != null ? payload.Length : 0;
        byte[] header = new byte[5];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), len);

        await stream.WriteAsync(header.AsMemory(0, 5), ct).ConfigureAwait(false);

        if (len > 0 && payload != null)
        {
            int offset = 0;
            int chunkSize = 16384; // 16 KB chunks for socket stability
            while (offset < len)
            {
                int toWrite = Math.Min(chunkSize, len - offset);
                await stream.WriteAsync(payload.AsMemory(offset, toWrite), ct).ConfigureAwait(false);
                offset += toWrite;
            }
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously reads one full packet from stream. Returns null on stream disconnect.
    /// Non-blocking, cancellable, prevents WinRT StreamSocket deadlocks.
    /// </summary>
    public static async Task<PrimePacket?> ReadFromStreamAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[5];
        int read = 0;
        while (read < 5)
        {
            int r = await stream.ReadAsync(header.AsMemory(read, 5 - read), ct).ConfigureAwait(false);
            if (r <= 0) return null; // End of stream / connection closed
            read += r;
        }

        byte typeByte = header[0];
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
        if (length < 0 || length > 100 * 1024 * 1024) // 100 MB max protection
        {
            throw new InvalidDataException($"Invalid packet length: {length}");
        }

        byte[] payload = new byte[length];
        read = 0;
        while (read < length)
        {
            int r = await stream.ReadAsync(payload.AsMemory(read, length - read), ct).ConfigureAwait(false);
            if (r <= 0) return null;
            read += r;
        }

        return new PrimePacket((PacketType)typeByte, payload);
    }

    public static void WriteToStream(Stream stream, PacketType type, byte[] payload)

    {
        int len = payload != null ? payload.Length : 0;

        byte[] header = new byte[5];

        header[0] = (byte)type;

        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), len);

        stream.Write(header, 0, 5);

        if (len > 0 && payload != null)

        {
            int offset = 0;

            int chunkSize = 16384; // 16 KB chunks for socket stability

            while (offset < len)

            {
                int toWrite = Math.Min(chunkSize, len - offset);

                stream.Write(payload, offset, toWrite);

                offset += toWrite;

            }

        }

        stream.Flush();

    }

    /// <summary>

    /// Reads one full packet from stream. Returns null on stream disconnect.

    /// Uses atomic 5-byte header reading to prevent fragmentation disconnects.

    /// </summary>

    public static PrimePacket? ReadFromStream(Stream stream)

    {
        byte[] header = new byte[5];

        int read = 0;

        while (read < 5)

        {
            int r = stream.Read(header, read, 5 - read);

            if (r <= 0) return null; // End of stream / connection closed

            read += r;

        }

        byte typeByte = header[0];

        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));

        if (length < 0 || length > 100 * 1024 * 1024) // 100 MB max protection

        {
            throw new InvalidDataException($"Invalid packet length: {length}");

        }

        byte[] payload = new byte[length];

        read = 0;

        while (read < length)

        {
            int r = stream.Read(payload, read, length - read);

            if (r <= 0) return null;

            read += r;

        }

        return new PrimePacket((PacketType)typeByte, payload);

    }

    // ==========================================

    // Payload Formatters & Parsers

    // ==========================================

    public static byte[] BuildTextPayload(string msgId, string text, string? replyToId = null, string? replyToSender = null, string? replyToText = null)

    {
        var sb = new StringBuilder();

        sb.Append(msgId).Append(":::").Append(text);

        if (!string.IsNullOrEmpty(replyToId))

        {
            sb.Append(":::REPLY:::")

              .Append(replyToId).Append(":::")

              .Append(replyToSender ?? "").Append(":::")

              .Append(replyToText ?? "");

        }

        return Encoding.UTF8.GetBytes(sb.ToString());

    }

    public static (string msgId, string realText, string? replyToId, string? replyToSender, string? replyToText) ParseTextPayload(byte[] payload)

    {
        string raw = Encoding.UTF8.GetString(payload);

        string msgId = Guid.NewGuid().ToString();

        string body = raw;

        int sep = raw.IndexOf(":::");

        if (sep != -1)

        {
            msgId = raw.Substring(0, sep);

            body = raw.Substring(sep + 3);

        }

        string? rId = null, rSender = null, rText = null;

        int replySep = body.IndexOf(":::REPLY:::");

        if (replySep != -1)

        {
            string replyPayload = body.Substring(replySep + 11);

            body = body.Substring(0, replySep);

            string[] parts = replyPayload.Split(":::");

            if (parts.Length >= 1) rId = parts[0];

            if (parts.Length >= 2) rSender = parts[1];

            if (parts.Length >= 3) rText = parts[2];

        }

        return (msgId, body, rId, rSender, rText);

    }

    // Reaction format: msgId:::emoji:::authorLogin (or msgId:::REMOVE:::authorLogin)

    public static byte[] BuildReactionPayload(string msgId, string? reaction, string authorLogin)

    {
        string rx = string.IsNullOrWhiteSpace(reaction) ? "REMOVE" : reaction;

        string data = $"{msgId}:::{rx}:::{authorLogin}";

        return Encoding.UTF8.GetBytes(data);

    }

    public static (string msgId, string? reaction, string authorLogin) ParseReactionPayload(byte[] payload)

    {
        string raw = Encoding.UTF8.GetString(payload);

        string[] parts = raw.Split(":::");

        string msgId = parts.Length > 0 ? parts[0] : "";

        string? reaction = parts.Length > 1 ? parts[1] : null;

        if ("REMOVE".Equals(reaction, StringComparison.OrdinalIgnoreCase)) reaction = null;

        string author = parts.Length > 2 ? parts[2] : "";

        return (msgId, reaction, author);

    }

    public static byte[] BuildDeleteMessagePayload(string msgId)

    {
        return Encoding.UTF8.GetBytes(msgId);

    }

    public static string ParseDeleteMessagePayload(byte[] payload)

    {
        return Encoding.UTF8.GetString(payload).Trim();

    }

    public static byte[] BuildProfileUpdatePayload(string displayName, string username)

    {
        string content = $"name={displayName};login={username}";

        return Encoding.UTF8.GetBytes(content);

    }

    public static (string? displayName, string? username) ParseProfileUpdatePayload(byte[] payload)

    {
        string text = Encoding.UTF8.GetString(payload);

        string? name = null;

        string? login = null;

        string[] parts = text.Split(';');

        foreach (var p in parts)

        {
            if (p.StartsWith("name=")) name = p.Substring(5);

            else if (p.StartsWith("login=")) login = p.Substring(6);

        }

        return (name, login);

    }

    public static byte[] BuildHandshakePayload(string displayName, string username, bool isAck = false)

    {
        string prefix = isAck ? "HANDSHAKE_ACK:" : "HANDSHAKE:";

        string content = $"{prefix}name={displayName};login={username};version=100;features=vn;presence=ONLINE;device=pc;platform=desktop";

        return Encoding.UTF8.GetBytes(content);

    }

    public static (string? displayName, string? username, string? presence, bool isAck) ParseHandshakePayload(byte[] payload)

    {
        string text = Encoding.UTF8.GetString(payload);

        bool isAck = text.StartsWith("HANDSHAKE_ACK:", StringComparison.OrdinalIgnoreCase);

        string prefix = isAck ? "HANDSHAKE_ACK:" : "HANDSHAKE:";

        if (!text.StartsWith("HANDSHAKE:", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("HANDSHAKE_ACK:", StringComparison.OrdinalIgnoreCase))

            return (null, null, null, false);

        string data = text.Substring(prefix.Length).Trim();

        string? name = null;

        string? login = null;

        string? presence = null;

        if (data.Contains("login=", StringComparison.OrdinalIgnoreCase) || 

            data.Contains("name=", StringComparison.OrdinalIgnoreCase) || 

            data.Contains("presence=", StringComparison.OrdinalIgnoreCase))

        {
            string[] parts = data.Split(';');

            foreach (var p in parts)

            {
                if (p.StartsWith("login=", StringComparison.OrdinalIgnoreCase)) login = p.Substring(6).Trim();

                else if (p.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) name = p.Substring(5).Trim();

                else if (p.StartsWith("presence=", StringComparison.OrdinalIgnoreCase)) presence = p.Substring(9).Trim();

            }

        }

        else

        {
            name = data;

        }

        return (name, login, presence, isAck);

    }

    public static byte[] BuildFilePayload(string msgId, string fileName, byte[] fileBytes, string duration = "00:00", bool isVoice = false)
    {
        string msgType = isVoice ? "VOICE" : "FILE";
        string text = isVoice ? "Голосовое сообщение" : fileName;
        string header = $"{msgId}:::{fileName}:::{fileBytes.Length}:::{text}:::DURATION:::{duration}:::IS_VIDEO:::0:::MSG_TYPE:::{msgType}:::HEADER_END:::";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        byte[] combined = new byte[headerBytes.Length + fileBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
        Buffer.BlockCopy(fileBytes, 0, combined, headerBytes.Length, fileBytes.Length);
        return combined;
    }

    public static (string msgId, string fileName, string duration, bool isVoice, byte[] fileBytes) ParseFilePayload(byte[] payload)
    {
        int tagIdx = -1;
        byte[] tagBytes = Encoding.UTF8.GetBytes(":::HEADER_END:::");
        for (int i = 0; i <= payload.Length - tagBytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < tagBytes.Length; j++)
            {
                if (payload[i + j] != tagBytes[j]) { match = false; break; }
            }
            if (match) { tagIdx = i; break; }
        }

        if (tagIdx != -1)
        {
            string headerStr = Encoding.UTF8.GetString(payload, 0, tagIdx);
            int bodyStart = tagIdx + tagBytes.Length;
            byte[] fileBytes = new byte[payload.Length - bodyStart];
            Buffer.BlockCopy(payload, bodyStart, fileBytes, 0, fileBytes.Length);

            string[] parts = headerStr.Split(":::");
            string msgId = parts.Length > 0 ? parts[0] : Guid.NewGuid().ToString();
            string fileName = parts.Length > 1 ? parts[1] : "file.bin";
            string duration = "00:00";
            bool isVoice = false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "DURATION" && i + 1 < parts.Length) duration = parts[i + 1];
                if (parts[i] == "MSG_TYPE" && i + 1 < parts.Length && parts[i + 1] == "VOICE") isVoice = true;
            }

            return (msgId, fileName, duration, isVoice, fileBytes);
        }
        else
        {
            return (Guid.NewGuid().ToString(), "file.bin", "00:00", false, payload);
        }
    }

}
