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
    private long _nextLogIndex;

    // Mutex ensuring snapshot reads don't see uncommitted/unstable in-flight mutations
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private volatile bool _hasInFlightMutation;

    public bool HasInFlightMutation => _hasInFlightMutation;

    public CommandCoordinator(
        Guid datasetId,
        IAuthorityGate authorityGate,
        IBookingStore store,
        IReplicaSender replication,
        long initialLogIndex = 0,
        int queueCapacity = 256)
    {
        _datasetId = datasetId;
        _authorityGate = authorityGate;
        _store = store;
        _replication = replication;
        _nextLogIndex = initialLogIndex + 1;

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
            var seats = await _store.GetAllSeatsAsync(cancellationToken).ConfigureAwait(false);
            var stableIndex = _store.GetStableIndex();
            if (!_authorityGate.CanServe || _authorityGate.CurrentEpoch != epoch)
                throw new InvalidOperationException("Cluster is not ready to serve authoritative reads.");
            return new StableSnapshot(_datasetId, epoch, stableIndex, seats);
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
        var existing = await _store.FindByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (!_authorityGate.CanServe || _authorityGate.CurrentEpoch != epoch)
            return new CommandReply(TransportStatus.ClusterNotReady, null);
        if (existing is null)
            return new CommandReply(TransportStatus.NotFound, null);

        if (!string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return new CommandReply(TransportStatus.Rejected, null);
        }

        if (existing.IsStable && existing.LogIndex <= _store.GetStableIndex())
            return new CommandReply(TransportStatus.Final, existing.Result);

        return new CommandReply(TransportStatus.Unknown, null);
        }
        finally { _mutationLock.Release(); }
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

            // 1. Identity & Dedup validation
            var valResult = await CommandIdentityValidator.ValidateAndCheckDedupAsync(
                cmd,
                _datasetId,
                _store,
                _cts.Token).ConfigureAwait(false);

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

            // 2. Load current state for decision engine
            var seat = await _store.GetSeatAsync(cmd.Identity.Command.SeatNo, _cts.Token).ConfigureAwait(false);
            ReservationRecord? reservation = null;
            TicketRecord? ticket = null;

            if (cmd.Identity.Command.ReservationId.HasValue)
            {
                reservation = await _store.GetReservationAsync(cmd.Identity.Command.ReservationId.Value, _cts.Token).ConfigureAwait(false);
                if (reservation is not null)
                {
                    ticket = await _store.GetTicketByReservationAsync(reservation.ReservationId, _cts.Token).ConfigureAwait(false);
                }
            }

            var nextIndex = _nextLogIndex;
            var epoch = _authorityGate.CurrentEpoch;
            var writerId = _authorityGate.LeaderId;
            var utcNow = DateTime.UtcNow;

            var decision = BookingDecisionEngine.Decide(
                cmd.Identity.Command,
                cmd.Identity.OwnerId,
                seat,
                reservation,
                ticket,
                nextIndex,
                epoch,
                writerId,
                utcNow);

            // 3. Apply Local (Atomic SQL transaction -> LocalApplied)
            PersistedEvent persistedEvent;
            try
            {
                persistedEvent = await _store.ApplyLocalAsync(
                    cmd,
                    decision,
                    epoch,
                    writerId,
                    utcNow,
                    _cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _authorityGate.BlockUncertain();
                item.Completion.TrySetResult(new CommandReply(TransportStatus.Unknown, null));
                return;
            }
            _nextLogIndex = persistedEvent.LogIndex + 1;

            if (persistedEvent.DatasetId != _datasetId || persistedEvent.LogIndex != nextIndex ||
                persistedEvent.Epoch != epoch || persistedEvent.WriterId != writerId ||
                !persistedEvent.ResultPayload.Span.SequenceEqual(CanonicalProtocol.SerializeBusinessResult(decision.Result)))
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
            item.Completion.TrySetResult(new CommandReply(TransportStatus.Final, decision.Result));
        }
        finally
        {
            _hasInFlightMutation = false;
            _mutationLock.Release();
        }
    }

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
