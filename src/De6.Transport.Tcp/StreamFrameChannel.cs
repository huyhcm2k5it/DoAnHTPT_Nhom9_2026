using System.Text.Unicode;

namespace De6.Transport.Tcp;

/// <summary>
/// Length-prefixed UTF-8 frames over any duplex stream (NetworkStream today, SslStream once K2 adds TLS).
/// Rules: reads are exact (partial and coalesced reads handled), reads and writes are each serialized,
/// and ANY failure after bytes may have moved (protocol error, EOF, I/O error, cancellation during I/O)
/// closes the channel, because the byte stream can no longer be trusted to be on a frame boundary.
/// Cancelling while still waiting for the read/write turn is safe and does not close the channel.
/// This class does not authenticate: callers pass in a stream that is already authenticated (V12 section 15).
/// </summary>
public sealed class StreamFrameChannel : IFrameChannel
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _readTurn = new(1, 1);
    private readonly SemaphoreSlim _writeTurn = new(1, 1);
    private readonly byte[] _header = new byte[FrameCodec.HeaderLength];
    private int _faulted;
    private int _disposed;
    private int _streamClosed;

    public StreamFrameChannel(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
            throw new ArgumentException("Stream must be readable and writable.", nameof(stream));
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnusable();
        await _readTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnusable();
            try
            {
                return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await FaultAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _readTurn.Release();
        }
    }

    public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken)
    {
        ThrowIfUnusable();
        // Invalid payloads are the caller's bug: reject before touching the stream so the channel stays healthy.
        var frame = FrameCodec.Encode(utf8Json.Span);
        await _writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnusable();
            try
            {
                await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await FaultAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await CloseStreamOnceAsync().ConfigureAwait(false);
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var headerBytes = await ReadFullAsync(_header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0) throw new FrameChannelClosedException();
        if (headerBytes < FrameCodec.HeaderLength)
            throw new FrameProtocolException(FrameError.TruncatedHeader, $"Connection ended after {headerBytes} of {FrameCodec.HeaderLength} header bytes.");
        var length = FrameCodec.ParseLength(_header);
        var body = new byte[length];
        var bodyBytes = await ReadFullAsync(body, cancellationToken).ConfigureAwait(false);
        if (bodyBytes < length)
            throw new FrameProtocolException(FrameError.TruncatedBody, $"Connection ended after {bodyBytes} of {length} body bytes.");
        if (!Utf8.IsValid(body))
            throw new FrameProtocolException(FrameError.InvalidUtf8, "Frame body is not valid UTF-8.");
        return body;
    }

    /// <summary>Reads until the buffer is full or EOF; returns how many bytes arrived.</summary>
    private async ValueTask<int> ReadFullAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IsFaulted)
            throw new FrameProtocolException(FrameError.ChannelFaulted, "The channel was closed after an earlier failure.");
    }

    private async ValueTask FaultAsync()
    {
        Interlocked.Exchange(ref _faulted, 1);
        await CloseStreamOnceAsync().ConfigureAwait(false);
    }

    private ValueTask CloseStreamOnceAsync() =>
        Interlocked.Exchange(ref _streamClosed, 1) == 0 && !_leaveOpen ? _stream.DisposeAsync() : ValueTask.CompletedTask;
}
