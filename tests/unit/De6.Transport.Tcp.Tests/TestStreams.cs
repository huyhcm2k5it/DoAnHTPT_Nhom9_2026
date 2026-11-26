namespace De6.Transport.Tcp.Tests;

/// <summary>Serves scripted bytes to reads (at most <c>chunk</c> bytes per call) and records writes.</summary>
internal sealed class ScriptedStream : Stream
{
    private readonly byte[] _input;
    private readonly int _chunk;
    private int _position;
    public MemoryStream Written { get; } = new();
    public bool Disposed { get; private set; }

    public ScriptedStream(byte[] input, int chunk = int.MaxValue) { _input = input; _chunk = chunk; }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = Math.Min(Math.Min(buffer.Length, _chunk), _input.Length - _position);
        _input.AsSpan(_position, n).CopyTo(buffer.Span);
        _position += n;
        return ValueTask.FromResult(n);
    }

    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

/// <summary>Writes in tiny chunks with yields in between: without a writer lock, concurrent frames would interleave.</summary>
internal sealed class ChunkingWriteStream : Stream
{
    public MemoryStream Written { get; } = new();
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        for (var offset = 0; offset < buffer.Length; offset += 512)
        {
            var n = Math.Min(512, buffer.Length - offset);
            lock (Written) Written.Write(buffer.Span.Slice(offset, n));
            await Task.Yield();
        }
    }
}

/// <summary>Write blocks until released, or fails on demand.</summary>
internal sealed class GatedWriteStream : Stream
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailWrites { get; set; }
    public bool Disposed { get; private set; }
    public int WriteCalls { get; private set; }
    public Task WriteEntered => _entered.Task;
    public void Release() => _gate.TrySetResult();

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        WriteCalls++;
        _entered.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (FailWrites) throw new IOException("simulated write failure");
    }

    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

/// <summary>A read that never completes until cancelled.</summary>
internal sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
