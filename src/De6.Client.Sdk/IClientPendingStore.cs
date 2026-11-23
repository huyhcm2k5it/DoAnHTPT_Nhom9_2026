using De6.Contracts;

namespace De6.Client.Sdk;

public sealed record PendingCommand(CommandIdentity Identity, ReadOnlyMemory<byte> CanonicalCommand, DateTime SavedUtc);

public sealed record ResolvedCommand(Guid RequestId, ReadOnlyMemory<byte> ResultPayload, DateTime ResolvedUtc);

public interface IClientPendingStore : IAsyncDisposable
{
    ValueTask SaveBeforeSendAsync(CommandIdentity command, ReadOnlyMemory<byte> canonicalCommand, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CommandIdentity>> ReadUnresolvedAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PendingCommand>> ReadUnresolvedCommandsAsync(CancellationToken cancellationToken);
    ValueTask SaveResolvedAsync(Guid requestId, ReadOnlyMemory<byte> resultPayload, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ResolvedCommand>> ReadResolvedAsync(CancellationToken cancellationToken);
}
