using De6.Contracts;

namespace De6.Cluster.Replication;

public sealed record ReplicationOutcome(AppliedReceipt? MatchingReceipt)
{
    public bool IsUncertain => MatchingReceipt is null;
}

public interface IReplicaSender
{
    // A receipt must match node, both epochs, index, event and hash after durable Apply.
    // This interface does not implement matching, network I/O, or durability.
    Task<ReplicationOutcome> EnsureOneBackupAsync(PersistedEvent persistedEvent, TimeSpan deadline, CancellationToken shutdown);
}
