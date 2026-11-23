using System.Threading.Channels;
using De6.Contracts;
using De6.Cluster.Replication;

namespace De6.Booking.Domain;

public sealed class CommandCoordinator : ICommandCoordinator
{
    private sealed record MutationWorkItem(AuthenticatedCommand Command, TaskCompletionSource<CommandReply> Completion);

    private readonly Guid _datasetId;
    private readonly IAuthorityGate _authorityGate;
    private readonly IBookingStore _store;
    private readonly IReplicaSender _replication;
    private readonly Channel<MutationWorkItem> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    // Mutex ensuring snapshot reads don't see uncommitted/unstable in-flight mutations
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private volatile bool _hasInFlightMutation;

    public bool HasInFlightMutation => _hasInFlightMutation;

    public CommandCoordinator(
        Guid datasetId,
        IAuthorityGate authorityGate,
        IBookingStore store,
        IReplicaSender replication,
        int queueCapacity = 256)
    {
        _datasetId = datasetId;
        _authorityGate = authorityGate;
        _store = store;
        _replication = replication;

        var options = new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _queue = Channel.CreateBounded<MutationWorkItem>(options);
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public async Task<CommandReply> SubmitAsync(AuthenticatedCommand command, CancellationToken replyCancellation)
    {
        if (!_authorityGate.CanServe)
            return new CommandReply(TransportStatus.ClusterNotReady, null);

        var tcs = new TaskCompletionSource<CommandReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new MutationWorkItem(command, tcs);

        // Queue limit check (max 256)
        if (!_queue.Writer.TryWrite(item))
        {
            return new CommandReply(TransportStatus.RetryLater, null);
        }

        // Wait for worker completion or reply cancellation
        if (replyCancellation.CanBeCanceled)
        {
            if (replyCancellation.IsCancellationRequested)
            {
                return new CommandReply(TransportStatus.Unknown, null);
            }

            using var reg = replyCancellation.Register(() =>
            {
                // Cancellation only cancels waiting for the reply; it does NOT cancel the background work!
                tcs.TrySetCanceled(replyCancellation);
            });

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Return Unknown status to the caller if reply was canceled while operation proceeded
                return new CommandReply(TransportStatus.Unknown, null);
            }
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<StableSnapshot> GetSeatsAsync(CancellationToken cancellationToken = default)
    {
        if (!_authorityGate.CanServe)
            throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");

        // Wait if a mutation is currently in-flight between LocalApplied and Stable
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_authorityGate.CanServe)
                throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");
            var epoch = _authorityGate.CurrentEpoch;
            var stableIndex = _store.GetStableIndex();
            var snapshot = await _store.ReadSeatsSnapshotAsync(stableIndex, cancellationToken).ConfigureAwait(false);
            if (!_authorityGate.CanServe || _authorityGate.CurrentEpoch != epoch)
                throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");
            if (snapshot.DatasetId != _datasetId || snapshot.Epoch != epoch || snapshot.AppliedIndex != stableIndex)
                throw new InvalidOperationException("Seat snapshot does not match the authoritative stable state.");
            return snapshot;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<CommandReply> GetResultAsync(Guid requestId, string ownerId, CancellationToken cancellationToken = default)
    {
        if (!_authorityGate.CanServe)
            return new CommandReply(TransportStatus.ClusterNotReady, null);

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        if (!_authorityGate.CanServe)
            return new CommandReply(TransportStatus.ClusterNotReady, null);
            var epoch = _authorityGate.CurrentEpoch;
            var writerId = _authorityGate.LeaderId;
            var stableIndex = _store.GetStableIndex();
            var existing = await _store.FindByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
            if (!HasAuthority(epoch, writerId) || _store.GetStableIndex() != stableIndex)
                return new CommandReply(TransportStatus.ClusterNotReady, null);
            if (existing is null)
                return new CommandReply(TransportStatus.NotFound, null) { AppliedIndex = stableIndex };

            if (!string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
                return new CommandReply(TransportStatus.Rejected, null);

            if (existing.IsStable && existing.LogIndex <= stableIndex)
                return new CommandReply(TransportStatus.Final, existing.Result) { AppliedIndex = stableIndex };

            return new CommandReply(TransportStatus.Unknown, null);
        }
        finally { _mutationLock.Release(); }
    }

    public async Task<StableReservationsSnapshot> GetMyReservationsAsync(string authenticatedOwnerId, CancellationToken cancellationToken = default)
    {
        if (!_authorityGate.CanServe)
            throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");

        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedOwnerId);

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_authorityGate.CanServe)
                throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");

            var epoch = _authorityGate.CurrentEpoch;
            var stableIndex = _store.GetStableIndex();
            var snapshot = await _store.ReadReservationsSnapshotAsync(authenticatedOwnerId, stableIndex, cancellationToken).ConfigureAwait(false);

            if (!_authorityGate.CanServe || _authorityGate.CurrentEpoch != epoch)
                throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");

            if (snapshot.DatasetId != _datasetId || snapshot.Epoch != epoch || snapshot.AppliedIndex != stableIndex)
                throw new InvalidOperationException("Reservation snapshot does not match the authoritative stable state.");
            return snapshot;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    try
                    {
                        await ProcessSingleMutationAsync(item).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        item.Completion.TrySetResult(new CommandReply(TransportStatus.ClusterNotReady, null));
                        throw;
                    }
                    catch (Exception) when (!_cts.IsCancellationRequested)
                    {
                        _authorityGate.BlockUncertain();
                        item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        finally
        {
            while (reader.TryRead(out var item))
                item.Completion.TrySetResult(new CommandReply(TransportStatus.ClusterNotReady, null));
        }
    }

    private async Task ProcessSingleMutationAsync(MutationWorkItem item)
    {
        var cmd = item.Command;

        // Ensure single mutation is in flight
        await _mutationLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        _hasInFlightMutation = true;
        try
        {
            if (!_authorityGate.CanServe)
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.ClusterNotReady, null));
                return;
            }

            var epoch = _authorityGate.CurrentEpoch;
            var writerId = _authorityGate.LeaderId;

            // 1. Identity & Dedup validation
            var valResult = await CommandIdentityValidator.ValidateAndCheckDedupAsync(
                cmd,
                _datasetId,
                _store,
                _cts.Token).ConfigureAwait(false);

            if (!HasAuthority(epoch, writerId))
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.ClusterNotReady, null));
                return;
            }

            if (valResult.Status == ValidationStatus.RejectedBeforeEvent)
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Rejected, null, valResult.ErrorCode));
                return;
            }

            if (valResult.Status == ValidationStatus.DuplicateStable)
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Final, valResult.CachedResult));
                return;
            }

            if (valResult.Status == ValidationStatus.Conflict)
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Rejected, null, valResult.ErrorCode));
                return;
            }

            if (valResult.Status == ValidationStatus.DuplicateInFlight)
            {
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }

            var nextIndex = _store.GetStableIndex() + 1;
            var applied = await _store.DecideAndApplyAsync(
                cmd, new AcceptedAuthority(epoch, writerId), nextIndex, _cts.Token).ConfigureAwait(false);
            var persistedEvent = applied.Event;
            var result = CanonicalProtocol.DeserializeBusinessResult(persistedEvent.ResultPayload.Span);

            if (applied.ExistingResult || persistedEvent.DatasetId != _datasetId ||
                persistedEvent.LogIndex != nextIndex || persistedEvent.Epoch != epoch || persistedEvent.WriterId != writerId ||
                !HasAuthority(epoch, writerId))
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }

            // 4. Synchronous Replication to Backup (Quorum 2/3)
            AppliedReceipt? receipt;
            try
            {
                var outcome = await _replication.EnsureOneBackupAsync(persistedEvent, TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);
                receipt = outcome.MatchingReceipt;
            }
            catch (Exception)
            {
                // Timeout / Network loss on replication -> UNKNOWN
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }

            if (receipt is null || receipt.NodeId is < 1 or > 3 || receipt.NodeId == writerId ||
                receipt.AuthorityEpoch != epoch || receipt.EventEpoch != persistedEvent.Epoch ||
                receipt.LogIndex != persistedEvent.LogIndex || receipt.EventId != persistedEvent.EventId ||
                receipt.RecoveryRunId is not null || !receipt.EventHash.Span.SequenceEqual(persistedEvent.EventHash.Span))
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }

            // 5. Mark Stable and complete FINAL
            if (!_authorityGate.CanServe || _authorityGate.CurrentEpoch != epoch || _authorityGate.LeaderId != writerId)
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }
            try
            {
                await _store.MarkStableAsync(persistedEvent.LogIndex, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }
            if (_store.GetStableIndex() < persistedEvent.LogIndex || !_authorityGate.CanServe ||
                _authorityGate.CurrentEpoch != epoch || _authorityGate.LeaderId != writerId)
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }
            item.Completion.TrySetResult(new CommandReply(TransportStatus.Final, result));
        }
        finally
        {
            _hasInFlightMutation = false;
            _mutationLock.Release();
        }
    }

    private bool HasAuthority(long epoch, int writerId) =>
        _authorityGate.CanServe && _authorityGate.CurrentEpoch == epoch && _authorityGate.LeaderId == writerId;

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        _mutationLock.Dispose();
        _cts.Dispose();
    }
}
