using System.Globalization;
using System.Text;

namespace CloudPlayX.App;

/// <summary>
/// Reads an HTTP/1.1 "Transfer-Encoding: chunked" request body (Android sends uploads this way when the size is
/// unknown). <see cref="Finished"/> becomes true only after the terminating zero-size chunk, so a cut-off upload is
/// never mistaken for a complete one.
/// </summary>
public sealed class ChunkedBodyStream(Stream inner) : Stream
{
    private long _chunkLeft;
    private bool _needSize = true;

    public bool Finished { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (Finished || buffer.Length == 0) return 0;
        if (_needSize)
        {
            var line = await ReadLineAsync(ct) ?? throw new IOException("Upload ended inside a chunk header.");
            var hex = line.Split(';')[0].Trim();
            if (!long.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _chunkLeft) || _chunkLeft < 0)
                throw new InvalidDataException("Bad chunk size.");
            _needSize = false;
            if (_chunkLeft == 0)
            {
                while (!string.IsNullOrEmpty(await ReadLineAsync(ct))) { } // optional trailers, then the blank line
                Finished = true;
                return 0;
            }
        }
        int read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _chunkLeft)], ct);
        if (read == 0) throw new IOException("Upload ended inside a chunk.");
        _chunkLeft -= read;
        if (_chunkLeft == 0)
        {
            if (await ReadLineAsync(ct) != "") throw new InvalidDataException("Missing CRLF after chunk.");
            _needSize = true;
        }
        return read;
    }

    /// <summary>One CRLF-terminated line, read a byte at a time so no body bytes are consumed. Null at end of stream.</summary>
    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (sb.Length < 1024)
        {
            if (await inner.ReadAsync(one, ct) == 0) return null;
            if (one[0] == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)one[0]);
        }
        throw new InvalidDataException("Chunk header line too long.");
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
