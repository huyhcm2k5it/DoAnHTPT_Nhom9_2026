using System.Buffers.Binary;
using System.Text.Unicode;

namespace De6.Transport.Tcp;

/// <summary>
/// Wire format (V12 section 15): UInt32 big-endian N, then N bytes of UTF-8 JSON, 1 &lt;= N &lt;= 1 MiB.
/// </summary>
public static class FrameCodec
{
    public const int HeaderLength = 4;
    public const int MaxPayloadLength = 1024 * 1024;

    /// <summary>Builds header + payload as one buffer so a frame is always written with a single call.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 1 or > MaxPayloadLength)
            throw new ArgumentException($"Frame payload must be 1..{MaxPayloadLength} bytes, got {payload.Length}.", nameof(payload));
        if (!Utf8.IsValid(payload))
            throw new ArgumentException("Frame payload must be valid UTF-8.", nameof(payload));
        var frame = new byte[HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    /// <summary>Validates the 4-byte header BEFORE any payload buffer is allocated.</summary>
    public static int ParseLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
            throw new ArgumentException($"Header must be {HeaderLength} bytes.", nameof(header));
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > MaxPayloadLength)
            throw new FrameProtocolException(FrameError.InvalidLength, $"Frame length {length} is outside 1..{MaxPayloadLength}.");
        return (int)length;
    }
}
