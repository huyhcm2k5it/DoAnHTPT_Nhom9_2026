using De6.Contracts;

namespace De6.Cluster.Election;

public enum ElectionMessageKind { Election, Answer, Coordinator }
public sealed record ElectionRound(int InitiatorId, Guid RoundId);
public sealed record ElectionNotice(ElectionMessageKind Kind, int SenderId, Guid DatasetId, long AuthorityEpoch, ElectionRound Round);

public interface IElectionObserver
{
    ValueTask ReceiveAsync(AuthenticatedPeerIdentity authenticatedPeer, ElectionNotice notice,
        CancellationToken cancellationToken);
}

public interface IElectionTransport
{
    ValueTask SendAsync(int targetNodeId, ElectionNotice notice, CancellationToken cancellationToken);
}

public enum ElectionPhase
{
    Idle,
    WaitingForAnswer,
    WaitingForCoordinator,
    Candidate,
    Follower
}

public sealed record ElectionSnapshot(ElectionPhase Phase, ElectionRound? Round, int? CandidateId);
