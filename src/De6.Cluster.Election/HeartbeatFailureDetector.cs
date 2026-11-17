using De6.Contracts;

namespace De6.Cluster.Election;

/// <summary>Tracks authenticated peer heartbeats using monotonic elapsed time.</summary>
public sealed class HeartbeatFailureDetector
{
    private readonly object _sync = new();
    private readonly HashSet<int> _memberIds;
    private readonly int _localNodeId;
    private readonly Dictionary<int, long> _lastHeartbeat;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _suspectAfter;

    public HeartbeatFailureDetector(int localNodeId, IEnumerable<int> memberIds,
        TimeProvider? timeProvider = null, TimeSpan? heartbeatInterval = null, int missedHeartbeats = 3)
    {
        if (localNodeId is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(localNodeId));
        ArgumentNullException.ThrowIfNull(memberIds);
        if (missedHeartbeats < 1) throw new ArgumentOutOfRangeException(nameof(missedHeartbeats));

        var members = memberIds.ToHashSet();
        if (members.Count != 3 || !members.Order().SequenceEqual(new[] { 1, 2, 3 }) || !members.Contains(localNodeId))
            throw new ArgumentException("Membership must contain node IDs 1, 2 and 3.", nameof(memberIds));

        var interval = heartbeatInterval ?? TimeSpan.FromSeconds(1);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));

        _memberIds = members;
        _localNodeId = localNodeId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _suspectAfter = TimeSpan.FromTicks(checked(interval.Ticks * missedHeartbeats));
        var now = _timeProvider.GetTimestamp();
        _lastHeartbeat = members.Where(nodeId => nodeId != localNodeId).ToDictionary(nodeId => nodeId, _ => now);
    }

    public void Observe(AuthenticatedPeerIdentity peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        lock (_sync)
        {
            if (!_memberIds.Contains(peer.NodeId) || peer.NodeId == _localNodeId)
                throw new UnauthorizedAccessException("Heartbeat identity is outside the configured membership.");

            _lastHeartbeat[peer.NodeId] = _timeProvider.GetTimestamp();
        }
    }

    public IReadOnlyList<int> GetSuspectedNodeIds()
    {
        var now = _timeProvider.GetTimestamp();
        lock (_sync)
        {
            return _lastHeartbeat.Where(pair => _timeProvider.GetElapsedTime(pair.Value, now) >= _suspectAfter)
                .Select(pair => pair.Key).Order().ToArray();
        }
    }

    public TimeSpan SuspectAfter => _suspectAfter;
}
