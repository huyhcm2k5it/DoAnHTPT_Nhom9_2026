using De6.Contracts;

namespace De6.Cluster.Replication;

/// <summary>
/// Implements the synchronous Primary-to-Backup replication sender protocol (Q5).
/// Coordinates parallel distribution of PersistedEvent to backup nodes via IPeerChannel abstractions.
/// Verifies peer receipts strictly (LogIndex, EventId, Epoch, Hash, and matching target NodeId)
/// to achieve a 2/3 Quorum (Primary + at least 1 durable Backup) before advancing StableIndex.
/// 
/// Note on Integration Boundary:
/// Wire framing (TCP/TLS) is encapsulated behind IPeerChannel (implemented in De6.Transport.Tcp by Văn Khoa).
/// Storage and replay apply are executed by SqlReplicaApply (implemented in De6.Booking.Persistence by Huy).
/// </summary>
public sealed class SynchronousReplicaSender : IReplicaSender
{
    private readonly IReadOnlyList<IPeerChannel> _peerChannels;
    private readonly int _localNodeId;

    public SynchronousReplicaSender(IReadOnlyList<IPeerChannel> peerChannels, int localNodeId = 1)
    {
        _peerChannels = peerChannels ?? throw new ArgumentNullException(nameof(peerChannels));
        _localNodeId = localNodeId;
    }

    public async Task<ReplicationOutcome> EnsureOneBackupAsync(PersistedEvent persistedEvent, TimeSpan deadline, CancellationToken shutdown)
    {
        ArgumentNullException.ThrowIfNull(persistedEvent);

        if (_peerChannels.Count == 0)
        {
            return new ReplicationOutcome(null);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        cts.CancelAfter(deadline);

        // Fan out to all backup peer channels concurrently
        var tasks = _peerChannels.Select(async channel =>
        {
            try
            {
                var receipt = await channel.SendAppendApplyAsync(persistedEvent, deadline, cts.Token).ConfigureAwait(false);
                if (IsValidReceipt(receipt, persistedEvent, _localNodeId, channel.TargetNodeId))
                {
                    return receipt;
                }
            }
            catch (Exception)
            {
                // Network, peer timeout or serialization fault
            }
            return null;
        }).ToList();

        while (tasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completedTask);

            var receipt = await completedTask.ConfigureAwait(false);
            if (receipt is not null)
            {
                // Reached quorum (Primary + 1 backup durable)
                return new ReplicationOutcome(receipt);
            }
        }

        return new ReplicationOutcome(null);
    }

    private static bool IsValidReceipt(AppliedReceipt? receipt, PersistedEvent persistedEvent, int localNodeId, int expectedTargetNodeId)
    {
        if (receipt is null) return false;
        if (expectedTargetNodeId is < 1 or > 3 || expectedTargetNodeId == localNodeId) return false;
        if (receipt.NodeId != expectedTargetNodeId) return false;
        if (receipt.AuthorityEpoch != persistedEvent.Epoch) return false;
        if (receipt.EventEpoch != persistedEvent.Epoch) return false;
        if (receipt.LogIndex != persistedEvent.LogIndex) return false;
        if (receipt.EventId != persistedEvent.EventId) return false;
        if (receipt.RecoveryRunId is not null) return false;
        if (!receipt.EventHash.Span.SequenceEqual(persistedEvent.EventHash.Span)) return false;

        return true;
    }
}
