namespace De6.Transport.Tcp;

// Framing and TLS implementations are deliberately absent. A channel must authenticate
// before exposing frames, enforce 1..1 MiB, and serialize each socket's writes (V12 §15).
public interface IFrameChannel : IAsyncDisposable
{
    ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken);
    ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken);
}
