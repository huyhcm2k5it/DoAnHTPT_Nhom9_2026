namespace De6.Cluster.Election;

public enum ElectionMessageKind { Election, Answer, Coordinator }
public sealed record ElectionRound(int InitiatorId, Guid RoundId);
public sealed record ElectionNotice(ElectionMessageKind Kind, int SenderId, Guid DatasetId, long AuthorityEpoch, ElectionRound Round);

// No Bully algorithm is implemented. An election may nominate a candidate only;
// ClusterControl alone can activate after fencing, reconciliation and manifest readback.
public interface IElectionObserver
{
    ValueTask ReceiveAsync(ElectionNotice authenticatedNotice, CancellationToken cancellationToken);
}
