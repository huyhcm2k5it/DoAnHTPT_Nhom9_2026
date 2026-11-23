using De6.Booking.Domain;
using De6.Booking.Persistence;
using De6.Cluster.Control;
using De6.Cluster.Replication;
using De6.Contracts;

namespace De6.Server.Host;

public sealed record HostPersistence(ClusterControl Control, SqlBookingStore Store, ReplicaReceiver Receiver)
{
    public ICommandCoordinator CreateCoordinator(IReadOnlyList<IPeerChannel> peerChannels,
        Func<AcceptedAuthority, long, CancellationToken, Task<bool>>? backupPreflight = null)
    {
        var local = Control.Snapshot;
        return new CommandCoordinator(local.DatasetId, Control, new SqlCoordinatorStore(Store, Control),
            new SynchronousReplicaSender(peerChannels, local.NodeId), backupPreflight: backupPreflight);
    }

    public static HostPersistence Compose(SqlStoreOptions options, SqlStorageHealthReport health)
    {
        if (health.NodeId != options.NodeId || health.DatasetId != options.DatasetId)
            throw new InvalidDataException("SQL probe identity does not match the local Host.");
        var control = new ClusterControl(options.NodeId, options.DatasetId,
            health.AcceptedAuthority, health.LastAppliedIndex);
        var store = new SqlBookingStore(options, control, new AtomicBookingDecision());
        return new(control, store, new ReplicaReceiver(options.DatasetId, options.NodeId, store));
    }

    private sealed class AtomicBookingDecision : IBookingDecisionEngine
    {
        public StorageDecision Decide(CommandIdentity command, BookingState state, long nextIndex, DateTime utcNow)
        {
            var reservation = state.RequestedReservation is { } r
                ? new ReservationRecord(r.ReservationId, r.TripId, r.SeatNo, r.OwnerId, r.Status, r.CreatedUtc, r.UpdatedIndex)
                : null;
            var ticket = state.ExistingTicket is { } t
                ? new TicketRecord(t.TicketId, t.TripId, t.SeatNo, t.ReservationId, t.OwnerId, t.SoldUtc, t.CreatedIndex)
                : null;
            // The Domain decision is pure. SQL checks epoch and creator independently
            // and supplies the persisted event metadata after the decision.
            var decision = BookingDecisionEngine.Decide(command.Command, command.OwnerId, state.Seat,
                reservation, ticket, nextIndex, epoch: 0, writerId: 0, utcNow: utcNow);
            var reservationAfter = decision.ReservationAfter is { } after
                ? new ReservationData(after.ReservationId, after.TripId, after.SeatNo, after.OwnerId,
                    after.Status, after.CreatedUtc, after.UpdatedIndex)
                : null;
            var ticketCreated = decision.TicketCreated is { } created
                ? new TicketData(created.TicketId, created.ReservationId, created.TripId,
                    created.SeatNo, created.OwnerId, created.IssuedUtc, created.IssuedIndex)
                : null;
            return new(new(decision.Kind, decision.SeatAfter, reservationAfter, ticketCreated), decision.Result);
        }
    }
}
