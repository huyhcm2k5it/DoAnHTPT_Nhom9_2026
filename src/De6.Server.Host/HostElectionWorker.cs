using System.Collections.Concurrent;
using System.Text.Json;
using De6.Cluster.Control;
using De6.Cluster.Election;
using De6.Contracts;

namespace De6.Server.Host;

internal sealed class HostElectionWorker : IElectionTransport
{
    private readonly ClusterControl _control;
    private readonly int[] _peers;
    private readonly Func<int, object, CancellationToken, Task> _send;
    private readonly HeartbeatFailureDetector _detector;
    private readonly ConcurrentDictionary<int, ServingRole> _roles = new();
    private readonly BullyElection _election;
    private (ElectionSnapshot Snapshot, long Timestamp)? _phase;

    internal HostElectionWorker(ClusterControl control, NodeEndpoint[] membership,
        Func<int, object, CancellationToken, Task> send)
    {
        _control = control;
        _send = send;
        var local = control.Snapshot;
        _peers = membership.Where(node => node.NodeId != local.NodeId).Select(node => node.NodeId).ToArray();
        _detector = new HeartbeatFailureDetector(local.NodeId, membership.Select(node => node.NodeId));
        _election = new BullyElection(local.NodeId, local.DatasetId, local.AcceptedAuthority?.Epoch ?? 0, Eligible, this);
        _election.StateChanged += snapshot =>
        {
            if (snapshot is { CandidateId: { } candidate, Round: { } round } &&
                _control.Snapshot.AcceptedAuthority is { } authority)
            {
                _control.RecordElectionCandidate(candidate, round, authority.Epoch);
                Console.WriteLine($"Bully candidate S{candidate} at epoch {authority.Epoch}; no serving grant.");
            }
        };
    }

    internal async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var local = _control.Snapshot;
                if (local.AcceptedAuthority is not { } authority ||
                    local.Role is not (ServingRole.PrimaryReady or ServingRole.BackupReady))
                    continue;
                await Task.WhenAll(_peers.Select(peer => TrySendHeartbeatAsync(peer, local, authority, token)))
                    .ConfigureAwait(false);
                if (local.Role != ServingRole.BackupReady || !_detector.GetSuspectedNodeIds().Contains(authority.LeaderId))
                    continue;

                var state = _election.Snapshot;
                if (state.Phase == ElectionPhase.Idle)
                {
                    try { await _election.StartElectionAsync(token).ConfigureAwait(false); }
                    catch (InvalidOperationException) when (!Eligible(local.NodeId) ||
                        _election.Snapshot.Phase != ElectionPhase.Idle) { }
                }
                else if (state.Round is { } round && state.Phase is ElectionPhase.WaitingForAnswer or ElectionPhase.WaitingForCoordinator)
                {
                    var clock = TimeProvider.System;
                    if (_phase is not { } phase || phase.Snapshot != state)
                        _phase = (state, clock.GetTimestamp());
                    else if (clock.GetElapsedTime(phase.Timestamp) >= (state.Phase == ElectionPhase.WaitingForAnswer
                        ? BullyElection.AnswerTimeout : BullyElection.CoordinatorTimeout))
                        await _election.OnTimeoutAsync(round, state.Phase, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task<string?> HandleAsync(string type, ClientEnvelope envelope, AuthenticatedPeerIdentity peer,
        CancellationToken token)
    {
        if (type == WireMessageTypes.Heartbeat)
        {
            var authority = _control.Snapshot.AcceptedAuthority;
            var payload = envelope.Payload;
            if (authority is null || payload.GetProperty("epoch").GetInt64() != authority.Epoch ||
                payload.GetProperty("leaderId").GetInt32() != authority.LeaderId ||
                !Enum.TryParse<ServingRole>(payload.GetProperty("role").GetString(), out var role) ||
                (role == ServingRole.PrimaryReady && authority.LeaderId != peer.NodeId) ||
                (role == ServingRole.BackupReady && authority.LeaderId == peer.NodeId) ||
                role is not (ServingRole.PrimaryReady or ServingRole.BackupReady))
                return "{\"status\":\"REJECTED\"}";
            _roles[peer.NodeId] = role;
            _detector.Observe(peer);
            return "{\"status\":\"OK\"}";
        }

        if (type != WireMessageTypes.ElectionNotice) return null;
        var notice = envelope.Payload.GetProperty("notice").Deserialize<ElectionNotice>()
            ?? throw new InvalidDataException("Missing election notice.");
        if (!Enum.IsDefined(notice.Kind)) return "{\"status\":\"REJECTED\"}";
        await _election.ReceiveAsync(peer, notice, token).ConfigureAwait(false);
        return "{\"status\":\"OK\"}";
    }

    public async ValueTask SendAsync(int targetNodeId, ElectionNotice notice, CancellationToken cancellationToken) =>
        await _send(targetNodeId, new { type = WireMessageTypes.ElectionNotice, notice }, cancellationToken)
            .ConfigureAwait(false);

    private bool Eligible(int nodeId)
    {
        var local = _control.Snapshot;
        if (local.AcceptedAuthority is null || nodeId == local.AcceptedAuthority.LeaderId)
            return false;
        return nodeId == local.NodeId ? local.Role == ServingRole.BackupReady :
            _roles.TryGetValue(nodeId, out var role) && role == ServingRole.BackupReady &&
            !_detector.GetSuspectedNodeIds().Contains(nodeId);
    }

    private async Task TrySendHeartbeatAsync(int peer, ClusterControlSnapshot local, AcceptedAuthority authority,
        CancellationToken token)
    {
        try
        {
            await _send(peer, new { type = WireMessageTypes.Heartbeat, epoch = authority.Epoch,
                leaderId = authority.LeaderId, role = local.Role.ToString() }, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or
            System.Security.Authentication.AuthenticationException or OperationCanceledException or JsonException or InvalidDataException)
        {
            // Heartbeat suspicion is based on elapsed time since the last authenticated frame.
        }
    }
}
