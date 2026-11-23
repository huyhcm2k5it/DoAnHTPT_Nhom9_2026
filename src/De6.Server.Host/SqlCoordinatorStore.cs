using De6.Booking.Domain;
using De6.Booking.Persistence;
using De6.Cluster.Control;
using De6.Contracts;

namespace De6.Server.Host;

internal sealed class SqlCoordinatorStore(SqlBookingStore store, ClusterControl control) : Booking.Domain.IBookingStore
{
    public long GetStableIndex() => control.Snapshot.StableIndex;

    public async ValueTask<ExistingRequestRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var entry = await store.FindStableRequestAsync(requestId, GetStableIndex(), cancellationToken).ConfigureAwait(false);
        if (entry is null) return null;
        var saved = BookingEventCodec.Decode(entry);
        return new(saved.RequestId, saved.OwnerId, saved.CommandHash, entry.LogIndex, saved.Result, IsStable: true);
    }

    public async ValueTask<StableSnapshot> ReadSeatsSnapshotAsync(long expectedIndex, CancellationToken cancellationToken = default) =>
        await store.ReadStableSnapshotAsync(expectedIndex, cancellationToken).ConfigureAwait(false);

    public async ValueTask<StableReservationsSnapshot> ReadReservationsSnapshotAsync(string ownerId, long expectedIndex, CancellationToken cancellationToken = default)
    {
        var snapshot = await store.ReadMyReservationsAsync(ownerId, expectedIndex, cancellationToken).ConfigureAwait(false);
        var rows = snapshot.Reservations.Select(r => new ReservationRecord(r.ReservationId, r.TripId,
            r.SeatNo, r.OwnerId, r.Status, r.CreatedUtc, r.UpdatedIndex)).ToArray();
        return new(snapshot.DatasetId, snapshot.Epoch, snapshot.AppliedIndex, rows);
    }

    public Task<StoreApplyResult> DecideAndApplyAsync(AuthenticatedCommand command, AcceptedAuthority authority,
        long expectedNextIndex, CancellationToken cancellationToken = default) =>
        store.DecideAndApplyAsync(command.Identity, authority, expectedNextIndex, cancellationToken);

    public Task MarkStableAsync(long logIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        control.PublishStableIndex(logIndex);
        return Task.CompletedTask;
    }
}
