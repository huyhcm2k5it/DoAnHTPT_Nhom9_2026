using System.Security.Cryptography;
using De6.Booking.Domain;
using De6.Cluster.Election;
using De6.Contracts;

namespace De6.Cluster.Control;

public enum ServingRole
{
    Recovering,
    BackupReady,
    PrimaryReady,
    Maintenance,
    BlockedUncertain,
    StorageFault
}

public sealed record ClusterControlSnapshot(int NodeId, Guid DatasetId, ServingRole Role,
    bool ServingEnabled, AcceptedAuthority? AcceptedAuthority, int? ElectionCandidateId,
    ElectionRound? ElectionRound, long StableIndex, long LastAppliedIndex);

/// <summary>
/// Owns local serving authority and the SQL access gate. A process always starts closed.
/// Only recovery code in this assembly can apply a verified activation.
/// </summary>
public sealed class ClusterControl : IAuthorityGate, IStoreAccessGate
{
    private readonly object _sync = new();
    private readonly int _nodeId;
    private readonly Guid _datasetId;
    private readonly TimeProvider _timeProvider;
    private ServingRole _role = ServingRole.Recovering;
    private AcceptedAuthority? _acceptedAuthority;
    private int? _electionCandidateId;
    private ElectionRound? _electionRound;
    private long _stableIndex;
    private long _lastAppliedIndex;
    private int _activeLeases;
    private bool _exclusiveLeaseActive;
    private bool _primaryMutationActive;
    private int _snapshotLeases;
    private TaskCompletionSource? _leasesDrained;
    private RecoveryContext? _installedRecoveryContext;
    private DateTimeOffset _recoveryContextExpiresAt;

    public ClusterControl(int nodeId, Guid datasetId, AcceptedAuthority? persistedAuthority = null,
        long persistedLastAppliedIndex = 0, TimeProvider? timeProvider = null)
    {
        if (nodeId is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(nodeId));
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId is required.", nameof(datasetId));
        if (persistedLastAppliedIndex < 0) throw new ArgumentOutOfRangeException(nameof(persistedLastAppliedIndex));
        if (persistedAuthority is { } authority &&
            (authority.Epoch <= 0 || authority.LeaderId is < 1 or > 3))
            throw new ArgumentException("Persisted authority is invalid.", nameof(persistedAuthority));

        _nodeId = nodeId;
        _datasetId = datasetId;
        _acceptedAuthority = persistedAuthority is { Epoch: > 0 } ? persistedAuthority : null;
        _lastAppliedIndex = persistedLastAppliedIndex;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool CanServe
    {
        get { lock (_sync) return _role == ServingRole.PrimaryReady; }
    }

    public long CurrentEpoch
    {
        get { lock (_sync) return _acceptedAuthority?.Epoch ?? 0; }
    }

    public int LeaderId
    {
        get { lock (_sync) return _acceptedAuthority?.LeaderId ?? 0; }
    }

    public ClusterControlSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_nodeId, _datasetId, _role, _role == ServingRole.PrimaryReady,
                    _acceptedAuthority, _electionCandidateId, _electionRound, _stableIndex, _lastAppliedIndex);
        }
    }

    public void RecordElectionCandidate(int candidateId, ElectionRound round, long authorityEpoch)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (candidateId is < 1 or > 3 || round.InitiatorId != candidateId || round.RoundId == Guid.Empty)
            throw new ArgumentException("Candidate and election round are inconsistent.", nameof(round));

        lock (_sync)
        {
            if ((_acceptedAuthority?.Epoch ?? 0) != authorityEpoch)
                throw new InvalidOperationException("Election result belongs to a stale authority epoch.");
            _electionCandidateId = candidateId;
            _electionRound = round;
        }
    }

    public void RecordLocalAppliedIndex(long index)
    {
        lock (_sync)
        {
            if (index != _lastAppliedIndex + 1)
                throw new InvalidOperationException("LocalAppliedIndex must advance exactly one entry at a time.");
            _lastAppliedIndex = index;
        }
    }

    public void PublishStableIndex(long index)
    {
        lock (_sync)
        {
            if (_role != ServingRole.PrimaryReady || index != _stableIndex + 1 || index > _lastAppliedIndex)
                throw new InvalidOperationException("StableIndex requires the next locally applied entry on the active primary.");
            _stableIndex = index;
        }
    }

    public async Task BeginMaintenanceAsync(long expectedIndex, CancellationToken cancellationToken = default)
    {
        Task waitForLeases;
        lock (_sync)
        {
            if (expectedIndex != _lastAppliedIndex)
                throw new InvalidOperationException("Maintenance must freeze at LastAppliedIndex.");
            if (_role is ServingRole.StorageFault)
                throw new InvalidOperationException("Storage fault requires operator recovery before maintenance.");
            if (_role == ServingRole.PrimaryReady && _stableIndex != expectedIndex)
            {
                _role = ServingRole.BlockedUncertain;
                _installedRecoveryContext = null;
                throw new InvalidOperationException("An unstable local Apply must be resolved before primary maintenance.");
            }

            _role = ServingRole.Maintenance;
            _installedRecoveryContext = null;
            if (_activeLeases == 0) return;
            _leasesDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waitForLeases = _leasesDrained.Task;
        }

        await waitForLeases.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (_role != ServingRole.Maintenance || _activeLeases != 0)
                throw new InvalidOperationException("Node state changed while maintenance waited for active storage operations.");
            if (_lastAppliedIndex != expectedIndex)
            {
                _role = ServingRole.BlockedUncertain;
                throw new InvalidOperationException("An in-flight operation changed LastAppliedIndex during maintenance.");
            }
        }
    }

    public void InstallRecoveryContext(RecoveryContext context, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_sync)
        {
            if (_role is not (ServingRole.Recovering or ServingRole.Maintenance) || _activeLeases != 0)
                throw new InvalidOperationException("Recovery context can only be installed on a closed node.");
            ValidateRecoveryContext(context, expiresAt);
            _installedRecoveryContext = CopyContext(context);
            _recoveryContextExpiresAt = expiresAt;
        }
    }

    public void RevokeRecoveryContext(Guid runId)
    {
        lock (_sync)
        {
            if (_installedRecoveryContext?.RunId == runId)
                _installedRecoveryContext = null;
        }
    }

    public IDisposable EnterPrimary(AcceptedAuthority authority, long expectedNextIndex)
    {
        lock (_sync)
        {
            if (_role != ServingRole.PrimaryReady || _acceptedAuthority != authority ||
                _stableIndex != _lastAppliedIndex || expectedNextIndex != _lastAppliedIndex + 1 || _primaryMutationActive ||
                _exclusiveLeaseActive || _snapshotLeases != 0)
                throw new InvalidOperationException("Primary mutation is not authorized by the current control state.");

            _primaryMutationActive = true;
            _activeLeases++;
            return new Lease(() => ReleaseLease(LeaseKind.PrimaryMutation));
        }
    }

    public IDisposable EnterAppend(AuthenticatedPeerAuthority peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        lock (_sync)
        {
            if (_role != ServingRole.BackupReady || _acceptedAuthority != peer.Authority ||
                peer.SenderNodeId == _nodeId || peer.SenderNodeId != peer.Authority.LeaderId ||
                _exclusiveLeaseActive || _activeLeases != 0)
                throw new InvalidOperationException("Append peer is not authorized by the backup's accepted authority.");

            _activeLeases++;
            _exclusiveLeaseActive = true;
            return new Lease(() => ReleaseLease(LeaseKind.Exclusive));
        }
    }

    public IDisposable EnterReplay(AuthenticatedPeerIdentity authenticatedSource, RecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(authenticatedSource);
        ArgumentNullException.ThrowIfNull(context);
        if (authenticatedSource.NodeId != context.SourceNodeId)
            throw new UnauthorizedAccessException("Replay source does not match the authenticated peer identity.");

        lock (_sync)
        {
            if (_role is ServingRole.PrimaryReady or ServingRole.BackupReady ||
                _activeLeases != 0 || _exclusiveLeaseActive ||
                _installedRecoveryContext is null ||
                _timeProvider.GetUtcNow() >= _recoveryContextExpiresAt ||
                context.TargetEpoch < (_acceptedAuthority?.Epoch ?? 0) ||
                !SameContext(_installedRecoveryContext, context))
                throw new InvalidOperationException("Replay requires the exact unexpired context installed on this closed node.");

            _activeLeases++;
            _exclusiveLeaseActive = true;
            return new Lease(() => ReleaseLease(LeaseKind.Exclusive));
        }
    }

    public IDisposable EnterSnapshot(long expectedIndex)
    {
        lock (_sync)
        {
            if (_role != ServingRole.PrimaryReady || _stableIndex != _lastAppliedIndex ||
                expectedIndex != _stableIndex || _primaryMutationActive || _exclusiveLeaseActive)
                throw new InvalidOperationException("Snapshot requires a stable primary prefix at the requested index.");

            _snapshotLeases++;
            _activeLeases++;
            return new Lease(() => ReleaseLease(LeaseKind.Snapshot));
        }
    }

    public IDisposable EnterRecoveryInspection()
    {
        lock (_sync)
        {
            if (_role is ServingRole.PrimaryReady or ServingRole.BackupReady || _activeLeases != 0 || _exclusiveLeaseActive)
                throw new InvalidOperationException("Recovery inspection requires a closed node with no active storage operation.");

            _activeLeases++;
            _exclusiveLeaseActive = true;
            return new Lease(() => ReleaseLease(LeaseKind.Exclusive));
        }
    }

    public IDisposable EnterMaintenance(long expectedIndex)
    {
        lock (_sync)
        {
            if (_role != ServingRole.Maintenance || expectedIndex != _lastAppliedIndex ||
                _activeLeases != 0 || _exclusiveLeaseActive)
                throw new InvalidOperationException("Maintenance lease requires a closed and frozen node at the requested index.");

            _activeLeases++;
            _exclusiveLeaseActive = true;
            return new Lease(() => ReleaseLease(LeaseKind.Exclusive));
        }
    }

    public void Block(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A block reason is required.", nameof(reason));
        lock (_sync)
        {
            _role = ServingRole.BlockedUncertain;
            _installedRecoveryContext = null;
        }
    }

    public void BlockUncertain() => Block("UNCERTAIN_OPERATION");

    public void ApplyVerifiedActivation(RecoveryActivationAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var manifest = authorization.Manifest;
        manifest.Validate();
        if (manifest.DatasetId != _datasetId || !manifest.ParticipantNodeIds.Contains(_nodeId))
            throw new UnauthorizedAccessException("The verified recovery manifest does not authorize this dataset and node.");

        lock (_sync)
        {
            if (_activeLeases != 0 || _role is not (ServingRole.Recovering or ServingRole.Maintenance))
                throw new InvalidOperationException("Activation requires a recovering or maintenance node with no active storage operation.");
            if (_lastAppliedIndex != manifest.TargetIndex)
                throw new InvalidOperationException("Activation requires this node to have applied the exact manifest target prefix; replay or archive repair must finish first.");
            if (manifest.Kind == RecoveryRunKind.Bootstrap)
            {
                if (_acceptedAuthority is not null && _acceptedAuthority != manifest.NewAuthority)
                    throw new InvalidOperationException("Bootstrap cannot replace persisted authority on an existing dataset.");
            }
            else if (_acceptedAuthority != manifest.PreviousAuthority && _acceptedAuthority != manifest.NewAuthority)
            {
                throw new InvalidOperationException("Persisted authority is neither the run's old authority nor its exact read-back authority.");
            }
            if (_electionCandidateId is { } electionCandidate && electionCandidate != manifest.NewAuthority.LeaderId)
                throw new InvalidOperationException("The verified manifest candidate differs from the local election result.");

            _role = manifest.NewAuthority.LeaderId == _nodeId ? ServingRole.PrimaryReady : ServingRole.BackupReady;
            _acceptedAuthority = manifest.NewAuthority;
            _stableIndex = manifest.TargetIndex;
            _electionCandidateId = null;
            _electionRound = null;
            _installedRecoveryContext = null;
        }
    }

    private void ValidateRecoveryContext(RecoveryContext context, DateTimeOffset expiresAt)
    {
        if (context.RunId == Guid.Empty || context.DatasetId != _datasetId ||
            context.SourceNodeId is < 1 or > 3 || context.SourceNodeId == _nodeId ||
            context.TargetNodeId != _nodeId || context.TargetLeaderId is < 1 or > 3 ||
            context.TargetEpoch <= 0 || context.BaseIndex < 0 || context.TargetIndex < context.BaseIndex ||
            context.BaseHash.Length != 32 || context.HeadHash.Length != 32 ||
            expiresAt <= _timeProvider.GetUtcNow())
            throw new ArgumentException("Recovery context is invalid for this node or has expired.", nameof(context));
    }

    private static RecoveryContext CopyContext(RecoveryContext context)
        => context with { BaseHash = context.BaseHash.ToArray(), HeadHash = context.HeadHash.ToArray() };

    private static bool SameContext(RecoveryContext installed, RecoveryContext supplied)
        => installed.RunId == supplied.RunId && installed.DatasetId == supplied.DatasetId &&
           installed.SourceNodeId == supplied.SourceNodeId && installed.TargetNodeId == supplied.TargetNodeId &&
           installed.TargetEpoch == supplied.TargetEpoch && installed.TargetLeaderId == supplied.TargetLeaderId &&
           installed.BaseIndex == supplied.BaseIndex && installed.TargetIndex == supplied.TargetIndex &&
           CryptographicOperations.FixedTimeEquals(installed.BaseHash.Span, supplied.BaseHash.Span) &&
           CryptographicOperations.FixedTimeEquals(installed.HeadHash.Span, supplied.HeadHash.Span);

    private void ReleaseLease(LeaseKind kind)
    {
        TaskCompletionSource? drained = null;
        lock (_sync)
        {
            if (kind == LeaseKind.PrimaryMutation) _primaryMutationActive = false;
            if (kind == LeaseKind.Snapshot) _snapshotLeases--;
            if (kind == LeaseKind.Exclusive) _exclusiveLeaseActive = false;
            _activeLeases--;
            if (_activeLeases == 0 && _leasesDrained is not null)
            {
                drained = _leasesDrained;
                _leasesDrained = null;
            }
        }
        drained?.TrySetResult();
    }

    private enum LeaseKind { PrimaryMutation, Snapshot, Exclusive }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
