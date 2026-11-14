using De6.Contracts;

namespace De6.Client.Sdk;

// Future implementation: exclusive store lock, atomic replace + flush BEFORE send,
// same DatasetId/OwnerId/RequestId on all retries. No implementation is supplied yet.
public interface IClientPendingStore : IAsyncDisposable
{
    ValueTask SaveBeforeSendAsync(CommandIdentity command, ReadOnlyMemory<byte> canonicalCommand, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CommandIdentity>> ReadUnresolvedAsync(CancellationToken cancellationToken);
    ValueTask SaveResolvedAsync(Guid requestId, ReadOnlyMemory<byte> resultPayload, CancellationToken cancellationToken);
}
