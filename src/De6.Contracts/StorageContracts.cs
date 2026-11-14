namespace De6.Contracts;

public enum ReservationStatus { Held, Confirmed, Cancelled }
public enum DecisionKind { HeldCreated, SoldConfirmed, HoldCancelled, NoChange }
public sealed record ReservationData(Guid ReservationId, int TripId, int SeatNo, string OwnerId,
    ReservationStatus Status, DateTime CreatedUtc, long UpdatedIndex);
public sealed record TicketData(Guid TicketId, Guid ReservationId, int TripId, int SeatNo, string OwnerId,
    DateTime SoldUtc, long CreatedIndex);
public sealed record BookingState(SeatView Seat, ReservationData? RequestedReservation,
    ReservationData? CurrentReservation, TicketData? ExistingTicket, TicketData? CurrentTicket = null);
public sealed record DecisionPatch(DecisionKind Kind, SeatView? SeatAfter,
    ReservationData? ReservationAfter, TicketData? TicketCreated);
public sealed record BookingDecision(DecisionPatch Patch, BusinessResult Result);
public sealed record BookingEvent(int ProtocolVersion, Guid DatasetId, long LogIndex, long Epoch, int WriterId,
    Guid EventId, string PreviousHash, Guid RequestId, string OwnerId, string CommandHash,
    CommandData CommandData, long BeforeSeatVersion, DecisionPatch Decision, BusinessResult Result);
public sealed record AuthenticatedPeerAuthority(int SenderNodeId, AcceptedAuthority Authority);
public sealed record StoreApplyResult(PersistedEvent Event, bool ExistingResult);
public sealed record StoredResultSnapshot(long AppliedIndex, Guid RequestId, string OwnerId,
    long LogIndex, ReadOnlyMemory<byte> ResultPayload);
public sealed record ReservationsSnapshot(long AppliedIndex, IReadOnlyList<ReservationData> Reservations);

/// <summary>Pure synchronous decision; runs on SQL state within the primary transaction. No I/O.</summary>
public interface IBookingDecisionEngine
{
    BookingDecision Decide(CommandIdentity command, BookingState state, long nextIndex, DateTime utcNow);
}

/// <summary>
/// Trusted adapter supplied by Cluster.Control/coordinator. No default-open implementation.
/// Leases synchronize freeze/grants and stay held across await until disposed.
/// EnterSnapshot must verify PRIMARY_READY and StableIndex == LastAppliedIndex == expectedIndex.
/// EnterReplay must verify a locally installed, unexpired RecoveryContext on a closed node.
/// Block must be safe to call while a lease is held. Store cannot prove network stability itself.
/// </summary>
public interface IStoreAccessGate
{
    IDisposable EnterPrimary(AcceptedAuthority authority, long expectedNextIndex);
    IDisposable EnterAppend(AuthenticatedPeerAuthority peer);
    IDisposable EnterReplay(RecoveryContext context);
    IDisposable EnterSnapshot(long expectedIndex);
    IDisposable EnterRecoveryInspection();
    void Block(string reason);
}

public interface IBookingStore
{
    Task<StoreApplyResult> DecideAndApplyAsync(CommandIdentity command, AcceptedAuthority authority,
        long expectedNextIndex, CancellationToken cancellation = default);
    Task<AppliedReceipt> AppendAndApplyAsync(PersistedEvent entry, AuthenticatedPeerAuthority peer,
        CancellationToken cancellation = default);
    Task<AppliedReceipt> ApplyReplayEntryAsync(PersistedEvent entry, RecoveryContext context,
        CancellationToken cancellation = default);
    Task<StableSnapshot> ReadStableSnapshotAsync(long expectedIndex, CancellationToken cancellation = default);
    Task<StoredResultSnapshot?> ReadStableResultAsync(Guid requestId, string owner, long expectedIndex,
        CancellationToken cancellation = default);
    Task<ReservationsSnapshot> ReadMyReservationsAsync(string owner, long expectedIndex,
        CancellationToken cancellation = default);
}
