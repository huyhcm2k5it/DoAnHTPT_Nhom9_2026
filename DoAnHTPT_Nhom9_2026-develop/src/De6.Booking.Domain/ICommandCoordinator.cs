using De6.Contracts;

namespace De6.Booking.Domain;

public interface IAuthorityGate
{
    bool CanServe { get; }
    long CurrentEpoch { get; }
    int LeaderId { get; }
    void BlockUncertain();
}

public interface IBookingStore : IRequestResultLookup
{
    ValueTask<SeatView> GetSeatAsync(int seatNo, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<SeatView>> GetAllSeatsAsync(CancellationToken cancellationToken = default);
    ValueTask<ReservationRecord?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    ValueTask<TicketRecord?> GetTicketByReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReservationRecord>> GetReservationsByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);
    // Implementations must read rows and metadata in one transaction and reject an index mismatch.
    ValueTask<StableReservationsSnapshot> ReadReservationsSnapshotAsync(string ownerId, long expectedIndex, CancellationToken cancellationToken = default);

    Task<PersistedEvent> ApplyLocalAsync(
        AuthenticatedCommand command,
        BookingDecision decision,
        long epoch,
        int writerId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task MarkStableAsync(long logIndex, CancellationToken cancellationToken = default);
}

public interface ICommandCoordinator : IAsyncDisposable
{
    // Cancellation stops waiting for the reply; it cannot abandon an operation after LocalApplied.
    Task<CommandReply> SubmitAsync(AuthenticatedCommand command, CancellationToken replyCancellation);
    Task<StableSnapshot> GetSeatsAsync(CancellationToken cancellationToken = default);
    Task<CommandReply> GetResultAsync(Guid requestId, string ownerId, CancellationToken cancellationToken = default);
    Task<StableReservationsSnapshot> GetMyReservationsAsync(string authenticatedOwnerId, CancellationToken cancellationToken = default);
}
