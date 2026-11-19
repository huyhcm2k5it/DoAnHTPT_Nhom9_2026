using De6.Contracts;

namespace De6.Cluster.Election;

/// <summary>
/// Runs the Bully message state machine. The caller schedules OnTimeoutAsync using a
/// monotonic timer and keeps this result separate from ClusterControl's serving role.
/// </summary>
public sealed class BullyElection : IElectionObserver
{
    private const int MaximumRememberedRounds = 1024;
    private readonly object _sync = new();
    private readonly int _nodeId;
    private readonly Guid _datasetId;
    private readonly long _authorityEpoch;
    private readonly Func<int, bool> _isEligible;
    private readonly IElectionTransport _transport;
    private readonly HashSet<ElectionRound> _answeredRounds = [];
    private readonly Queue<ElectionRound> _roundOrder = [];
    private ElectionSnapshot _snapshot = new(ElectionPhase.Idle, null, null);

    public BullyElection(int nodeId, Guid datasetId, long authorityEpoch,
        Func<int, bool> isEligible, IElectionTransport transport)
    {
        if (nodeId is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(nodeId));
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId is required.", nameof(datasetId));
        if (authorityEpoch < 0) throw new ArgumentOutOfRangeException(nameof(authorityEpoch));

        _nodeId = nodeId;
        _datasetId = datasetId;
        _authorityEpoch = authorityEpoch;
        _isEligible = isEligible ?? throw new ArgumentNullException(nameof(isEligible));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public static TimeSpan AnswerTimeout => TimeSpan.FromSeconds(2);
    public static TimeSpan CoordinatorTimeout => TimeSpan.FromSeconds(5);
    public static TimeSpan MaximumRestartBackoff => TimeSpan.FromMilliseconds(500);

    public event Action<ElectionSnapshot>? StateChanged;
    public event Action<int, Exception>? SendFailed;

    public ElectionSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public async Task<ElectionRound> StartElectionAsync(CancellationToken cancellationToken = default)
    {
        ElectionRound round;
        ElectionSnapshot snapshot;
        ElectionNotice[] notices;
        lock (_sync)
        {
            if (!_isEligible(_nodeId))
                throw new InvalidOperationException("A node outside the admitted membership cannot start an election.");
            if (_snapshot.Phase is ElectionPhase.WaitingForAnswer or ElectionPhase.WaitingForCoordinator)
                throw new InvalidOperationException("This node already has an election round in progress.");

            round = NewRound();
            var higherNodes = EligibleNodes().Where(nodeId => nodeId > _nodeId).ToArray();
            if (higherNodes.Length == 0)
            {
                _snapshot = new ElectionSnapshot(ElectionPhase.Candidate, round, _nodeId);
                notices = CoordinatorNotices(round);
            }
            else
            {
                _snapshot = new ElectionSnapshot(ElectionPhase.WaitingForAnswer, round, null);
                notices = [Notice(ElectionMessageKind.Election, round)];
            }
            snapshot = _snapshot;
        }

        StateChanged?.Invoke(snapshot);
        await SendAllAsync(notices, cancellationToken).ConfigureAwait(false);
        return round;
    }

    public async ValueTask ReceiveAsync(AuthenticatedPeerIdentity authenticatedPeer,
        ElectionNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPeer);
        ArgumentNullException.ThrowIfNull(notice);
        ValidateNotice(authenticatedPeer, notice);
        if (!_isEligible(_nodeId)) return;

        List<ElectionNotice> outbound = [];
        ElectionSnapshot? changedSnapshot = null;
        lock (_sync)
        {
            switch (notice.Kind)
            {
                case ElectionMessageKind.Election:
                    outbound.Add(Notice(ElectionMessageKind.Answer, notice.Round));
                    var firstTime = RememberAnswer(notice.Round);
                    if (firstTime && notice.SenderId < _nodeId && _isEligible(_nodeId) &&
                        ShouldStartOwnRoundAfter(notice.Round))
                    {
                        changedSnapshot = BeginRoundLocked(outbound);
                    }
                    else if (firstTime && notice.SenderId > _nodeId && IsNewerHigherRound(notice.Round))
                    {
                        _snapshot = new ElectionSnapshot(ElectionPhase.WaitingForCoordinator, notice.Round, null);
                        changedSnapshot = _snapshot;
                    }
                    break;

                case ElectionMessageKind.Answer:
                    if (_snapshot.Phase == ElectionPhase.WaitingForAnswer &&
                        _snapshot.Round == notice.Round && notice.SenderId > _nodeId)
                    {
                        _snapshot = new ElectionSnapshot(ElectionPhase.WaitingForCoordinator, notice.Round, null);
                        changedSnapshot = _snapshot;
                    }
                    break;

                case ElectionMessageKind.Coordinator:
                    if (CanAcceptCoordinator(notice))
                    {
                        _snapshot = new ElectionSnapshot(ElectionPhase.Follower, notice.Round, notice.SenderId);
                        changedSnapshot = _snapshot;
                    }
                    break;

                default:
                    throw new InvalidDataException("Unknown election message kind.");
            }
        }

        if (changedSnapshot is not null) StateChanged?.Invoke(changedSnapshot);
        await SendAllAsync(outbound, cancellationToken).ConfigureAwait(false);
    }

    public async Task OnTimeoutAsync(ElectionRound round, ElectionPhase timedOutPhase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(round);
        ElectionNotice[] notices;
        ElectionSnapshot? changedSnapshot = null;
        var restart = false;

        lock (_sync)
        {
            if (_snapshot.Round != round || _snapshot.Phase != timedOutPhase) return;

            if (timedOutPhase == ElectionPhase.WaitingForAnswer)
            {
                if (_isEligible(_nodeId))
                {
                    _snapshot = new ElectionSnapshot(ElectionPhase.Candidate, round, _nodeId);
                    notices = CoordinatorNotices(round);
                }
                else
                {
                    _snapshot = new ElectionSnapshot(ElectionPhase.Idle, null, null);
                    notices = [];
                }
                changedSnapshot = _snapshot;
            }
            else if (timedOutPhase == ElectionPhase.WaitingForCoordinator)
            {
                notices = [];
                if (_isEligible(_nodeId))
                {
                    _snapshot = new ElectionSnapshot(ElectionPhase.Idle, null, null);
                    changedSnapshot = _snapshot;
                    restart = true;
                }
                else
                {
                    _snapshot = new ElectionSnapshot(ElectionPhase.Idle, null, null);
                    changedSnapshot = _snapshot;
                }
            }
            else
            {
                return;
            }
        }

        if (changedSnapshot is not null) StateChanged?.Invoke(changedSnapshot);
        await SendAllAsync(notices, cancellationToken).ConfigureAwait(false);
        if (restart)
        {
            var backoffMilliseconds = Random.Shared.Next(0, (int)MaximumRestartBackoff.TotalMilliseconds + 1);
            await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds), cancellationToken).ConfigureAwait(false);
            await StartElectionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private ElectionSnapshot BeginRoundLocked(List<ElectionNotice> outbound)
    {
        var round = NewRound();
        var higherNodes = EligibleNodes().Where(nodeId => nodeId > _nodeId).ToArray();
        if (higherNodes.Length == 0)
        {
            _snapshot = new ElectionSnapshot(ElectionPhase.Candidate, round, _nodeId);
            outbound.AddRange(CoordinatorNotices(round));
        }
        else
        {
            _snapshot = new ElectionSnapshot(ElectionPhase.WaitingForAnswer, round, null);
            if (higherNodes.Length > 0) outbound.Add(Notice(ElectionMessageKind.Election, round));
        }
        return _snapshot;
    }

    private bool ShouldStartOwnRoundAfter(ElectionRound receivedRound)
    {
        if (_snapshot.Phase == ElectionPhase.Candidate && _snapshot.CandidateId == _nodeId) return false;
        return _snapshot.Round is null || _snapshot.Round.InitiatorId < _nodeId ||
            (_snapshot.Round.InitiatorId == receivedRound.InitiatorId && _snapshot.Round == receivedRound &&
             _snapshot.Phase == ElectionPhase.Follower);
    }

    private bool IsNewerHigherRound(ElectionRound receivedRound)
    {
        if (_snapshot.Round is null || receivedRound.InitiatorId > _snapshot.Round.InitiatorId) return true;
        return receivedRound.InitiatorId == _snapshot.Round.InitiatorId &&
            receivedRound != _snapshot.Round &&
            _snapshot.Phase is ElectionPhase.WaitingForCoordinator or ElectionPhase.Follower;
    }

    private bool CanAcceptCoordinator(ElectionNotice notice)
    {
        if (!_isEligible(_nodeId) || notice.Round.InitiatorId != notice.SenderId || !_isEligible(notice.SenderId)) return false;
        if (EligibleNodes().Any(nodeId => nodeId > notice.SenderId)) return false;
        if (_snapshot.Round is { } current && current.InitiatorId > notice.SenderId) return false;
        if (_snapshot.Round is { } sameInitiator && sameInitiator.InitiatorId == notice.SenderId &&
            sameInitiator != notice.Round) return false;
        return true;
    }

    private void ValidateNotice(AuthenticatedPeerIdentity peer, ElectionNotice notice)
    {
        if (peer.NodeId is < 1 or > 3 || peer.NodeId == _nodeId || notice.SenderId != peer.NodeId)
            throw new UnauthorizedAccessException("Election sender does not match the authenticated peer certificate.");
        if (notice.DatasetId != _datasetId || notice.AuthorityEpoch != _authorityEpoch)
            throw new InvalidDataException("Election notice has a stale epoch or different dataset.");
        if (notice.Round is null || notice.Round.RoundId == Guid.Empty || notice.Round.InitiatorId is < 1 or > 3)
            throw new InvalidDataException("Election notice has an invalid round.");
        if (notice.Kind is ElectionMessageKind.Election or ElectionMessageKind.Coordinator &&
            notice.Round.InitiatorId != notice.SenderId)
            throw new InvalidDataException("Election initiator does not match its sender.");
        if (!_isEligible(notice.SenderId))
            throw new UnauthorizedAccessException("An unadmitted node cannot participate in an election.");
    }

    private bool RememberAnswer(ElectionRound round)
    {
        if (!_answeredRounds.Add(round)) return false;
        _roundOrder.Enqueue(round);
        if (_roundOrder.Count > MaximumRememberedRounds)
            _answeredRounds.Remove(_roundOrder.Dequeue());
        return true;
    }

    private ElectionRound NewRound() => new(_nodeId, Guid.NewGuid());

    private int[] EligibleNodes() => Enumerable.Range(1, 3).Where(_isEligible).ToArray();

    private ElectionNotice Notice(ElectionMessageKind kind, ElectionRound round)
        => new(kind, _nodeId, _datasetId, _authorityEpoch, round);

    private ElectionNotice[] CoordinatorNotices(ElectionRound round)
        => EligibleNodes().Any(nodeId => nodeId < _nodeId)
            ? [Notice(ElectionMessageKind.Coordinator, round)]
            : [];

    private async Task SendAllAsync(IEnumerable<ElectionNotice> notices, CancellationToken cancellationToken)
    {
        foreach (var notice in notices)
        {
            foreach (var targetNodeId in Recipients(notice))
            {
                try
                {
                    await _transport.SendAsync(targetNodeId, notice, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    SendFailed?.Invoke(targetNodeId, exception);
                }
            }
        }
    }

    private IEnumerable<int> Recipients(ElectionNotice notice)
    {
        if (notice.Kind == ElectionMessageKind.Answer) return [notice.Round.InitiatorId];
        if (notice.Kind == ElectionMessageKind.Election)
            return EligibleNodes().Where(nodeId => nodeId > _nodeId);
        return EligibleNodes().Where(nodeId => nodeId < _nodeId);
    }
}
