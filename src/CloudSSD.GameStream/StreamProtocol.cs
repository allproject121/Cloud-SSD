using System.Buffers.Binary;

namespace CloudSSD.GameStream;

/// <summary>
/// The wire format between the PC and the phone app once <c>/api/game/stream</c> has switched the connection over:
/// every message is <c>[type: 1 byte][length: 4 bytes little-endian][payload]</c>. Numbers inside payloads are
/// little-endian too (the phone reads them with a little-endian ByteBuffer).
/// </summary>
public static class StreamProtocol
{
    // PC → phone
    /// <summary>[capture time µs: 8][flags: 1, bit 0 = keyframe][H.264 Annex-B access unit]</summary>
    public const byte Video = 1;
    /// <summary>[capture time µs: 8][PCM 16-bit stereo 48 kHz]</summary>
    public const byte Audio = 2;
    /// <summary>UTF-8 JSON: width, height, fps, bitrateKbps, inFront, message.</summary>
    public const byte Info = 3;
    /// <summary>[the phone's ping time: 8][PC time µs: 8]</summary>
    public const byte Pong = 4;

    // phone → PC
    /// <summary>[count: 1] then per finger [id: 1][state: 1 = down, 2 = move, 3 = up][x: 2][y: 2], x/y 0..65535 across the picture.</summary>
    public const byte Touch = 16;
    /// <summary>[phone time: 8], answered with <see cref="Pong"/>.</summary>
    public const byte Ping = 17;
    /// <summary>The phone's decoder lost its place; the next picture must be a keyframe.</summary>
    public const byte NeedKeyframe = 18;
    public const byte Bye = 19;

    public const int HeaderSize = 5;
    /// <summary>Larger messages from the phone are refused: touches and pings are tiny.</summary>
    public const int MaxFromPhone = 4096;

    public const byte FingerDown = 1, FingerMove = 2, FingerUp = 3;

    public static void WriteHeader(Span<byte> into, byte type, int length)
    {
        into[0] = type;
        BinaryPrimitives.WriteInt32LittleEndian(into[1..], length);
    }

    /// <summary>One whole message as bytes; <paramref name="prefix"/> (e.g. a time stamp) goes before <paramref name="body"/>.</summary>
    public static byte[] Frame(byte type, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> body)
    {
        var bytes = new byte[HeaderSize + prefix.Length + body.Length];
        WriteHeader(bytes, type, prefix.Length + body.Length);
        prefix.CopyTo(bytes.AsSpan(HeaderSize));
        body.CopyTo(bytes.AsSpan(HeaderSize + prefix.Length));
        return bytes;
    }

    public static byte[] VideoFrame(long captureMicros, bool keyframe, ReadOnlySpan<byte> accessUnit)
    {
        Span<byte> prefix = stackalloc byte[9];
        BinaryPrimitives.WriteInt64LittleEndian(prefix, captureMicros);
        prefix[8] = keyframe ? (byte)1 : (byte)0;
        return Frame(Video, prefix, accessUnit);
    }

    public static byte[] AudioFrame(long captureMicros, ReadOnlySpan<byte> pcm)
    {
        Span<byte> prefix = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(prefix, captureMicros);
        return Frame(Audio, prefix, pcm);
    }

    public static byte[] PongFrame(long phoneTime, long pcMicros)
    {
        Span<byte> body = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(body, phoneTime);
        BinaryPrimitives.WriteInt64LittleEndian(body[8..], pcMicros);
        return Frame(Pong, default, body);
    }

    public readonly record struct Finger(byte Id, byte State, double X, double Y);

    /// <summary>Reads a <see cref="Touch"/> payload; null when it is malformed.</summary>
    public static Finger[]? ParseTouch(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1) return null;
        int count = payload[0];
        if (count > 10 || payload.Length != 1 + count * 6) return null;
        var fingers = new Finger[count];
        for (int i = 0; i < count; i++)
        {
            var f = payload.Slice(1 + i * 6, 6);
            if (f[1] is < FingerDown or > FingerUp) return null;
            fingers[i] = new Finger(f[0], f[1],
                BinaryPrimitives.ReadUInt16LittleEndian(f[2..]) / 65535.0,
                BinaryPrimitives.ReadUInt16LittleEndian(f[4..]) / 65535.0);
        }
        return fingers;
    }

    /// <summary>Builds a <see cref="Touch"/> message, as the phone does (used by tests).</summary>
    public static byte[] TouchFrame(params Finger[] fingers)
    {
        var body = new byte[1 + fingers.Length * 6];
        body[0] = (byte)fingers.Length;
        for (int i = 0; i < fingers.Length; i++)
        {
            var f = body.AsSpan(1 + i * 6);
            f[0] = fingers[i].Id;
            f[1] = fingers[i].State;
            BinaryPrimitives.WriteUInt16LittleEndian(f[2..], (ushort)Math.Round(Math.Clamp(fingers[i].X, 0, 1) * 65535));
            BinaryPrimitives.WriteUInt16LittleEndian(f[4..], (ushort)Math.Round(Math.Clamp(fingers[i].Y, 0, 1) * 65535));
        }
        return Frame(Touch, default, body);
    }

    /// <summary>Reads one message; null at the end of the stream. Throws <see cref="InvalidDataException"/> on a bad length.</summary>
    public static async Task<(byte Type, byte[] Payload)?> ReadAsync(Stream stream, int maxLength, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        if (!await ReadExactlyOrEndAsync(stream, header, ct)) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (length < 0 || length > maxLength) throw new InvalidDataException($"message of {length} bytes");
        var payload = new byte[length];
        if (length > 0 && !await ReadExactlyOrEndAsync(stream, payload, ct)) return null;
        return (header[0], payload);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
