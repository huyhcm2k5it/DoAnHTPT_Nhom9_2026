namespace De6.Contracts;

public sealed record ReservationData(Guid ReservationId, int TripId, int SeatNo, string OwnerId,
    ReservationStatus Status, DateTime CreatedUtc, long UpdatedIndex);
public sealed record TicketData(Guid TicketId, Guid ReservationId, int TripId, int SeatNo, string OwnerId,
    DateTime SoldUtc, long CreatedIndex);
public sealed record BookingState(SeatView Seat, ReservationData? RequestedReservation,
    ReservationData? CurrentReservation, TicketData? ExistingTicket, TicketData? CurrentTicket = null);
public sealed record DecisionPatch(DecisionKind Kind, SeatView? SeatAfter,
    ReservationData? ReservationAfter, TicketData? TicketCreated);
// Persistence's patch-oriented result is distinct from the coordinator's BookingDecision.
public sealed record StorageDecision(DecisionPatch Patch, BusinessResult Result);
public sealed record BookingEvent(int ProtocolVersion, Guid DatasetId, long LogIndex, long Epoch, int WriterId,
    Guid EventId, string PreviousHash, Guid RequestId, string OwnerId, string CommandHash,
    CommandData CommandData, long BeforeSeatVersion, DecisionPatch Decision, BusinessResult Result);
public sealed record AuthenticatedPeerAuthority(int SenderNodeId, AcceptedAuthority Authority);
public sealed record StoreApplyResult(PersistedEvent Event, bool ExistingResult);
public sealed record StoredResultSnapshot(long AppliedIndex, Guid RequestId, string OwnerId,
    long LogIndex, ReadOnlyMemory<byte> ResultPayload);
public sealed record ReservationsSnapshot(Guid DatasetId, long Epoch, long AppliedIndex, IReadOnlyList<ReservationData> Reservations);

/// <summary>Pure synchronous decision; runs on SQL state within the primary transaction. No I/O.</summary>
public interface IBookingDecisionEngine
{
    StorageDecision Decide(CommandIdentity command, BookingState state, long nextIndex, DateTime utcNow);
}

/// <summary>
/// Trusted adapter supplied by Cluster.Control/coordinator. No default-open implementation.
/// Leases synchronize freeze/grants and stay held across await until disposed.
/// EnterSnapshot must verify PRIMARY_READY and StableIndex == LastAppliedIndex == expectedIndex.
/// EnterReplay must verify a locally installed, unexpired RecoveryContext on a closed node.
/// EnterRecoveryInspection permits local evidence/audit reads while serving is closed; it does
/// not prove a frozen prefix or grant permission to rebuild.
/// EnterMaintenance is a trusted Control/Recovery grant: it must verify the node is closed,
/// freeze at expectedIndex == LastAppliedIndex, and exclude mutation plus authority/grant
/// transitions for the lease lifetime. It never enables serving. GetFrozenPrefix and rebuild
/// require this lease; AuditLocal/EnterRecoveryInspection alone are not sufficient.
/// Block must be safe to call while a lease is held. Store cannot prove network stability itself.
/// </summary>
public interface IStoreAccessGate
{
    IDisposable EnterPrimary(AcceptedAuthority authority, long expectedNextIndex);
    IDisposable EnterAppend(AuthenticatedPeerAuthority peer);
    IDisposable EnterReplay(AuthenticatedPeerIdentity authenticatedSource, RecoveryContext context);
    IDisposable EnterSnapshot(long expectedIndex);
    IDisposable EnterRecoveryInspection();
    IDisposable EnterMaintenance(long expectedIndex);
    void Block(string reason);
}

/// <summary>Updates the local applied cursor while SQL still holds the access lease.</summary>
public interface ILocalApplyProgress
{
    void RecordLocalAppliedIndex(long index);
}

public sealed record DurableResultView(Guid RequestId,string OwnerId,string CommandHash,long LogIndex,ReadOnlyMemory<byte> ResultPayload);
public sealed record StorageProjection(IReadOnlyList<SeatView> Seats,IReadOnlyList<ReservationData> Reservations,
    IReadOnlyList<TicketData> Tickets,IReadOnlyList<DurableResultView> Results);
public sealed record OracleViolation(string Invariant,long Count);
public sealed record LocalStorageAudit(int NodeId,Guid DatasetId,long LocalAppliedIndex,ReadOnlyMemory<byte> HeadHash,
    ReadOnlyMemory<byte> BusinessDigest,bool MatchesHistory,IReadOnlyList<OracleViolation> Violations);
public sealed record FrozenPrefixReport(int NodeId,Guid DatasetId,int SchemaVersion,long Index,
    ReadOnlyMemory<byte> HeadHash,ReadOnlyMemory<byte> BusinessDigest);

public interface IBookingStore
{
    Task<StoreApplyResult> DecideAndApplyAsync(CommandIdentity command, AcceptedAuthority authority,
        long expectedNextIndex, CancellationToken cancellation = default);
    Task<AppliedReceipt> AppendAndApplyAsync(PersistedEvent entry, AuthenticatedPeerAuthority peer,
        CancellationToken cancellation = default);
    Task<AppliedReceipt> ApplyReplayEntryAsync(PersistedEvent entry, AuthenticatedPeerIdentity authenticatedSource,
        RecoveryContext context,
        CancellationToken cancellation = default);
    Task<StableSnapshot> ReadStableSnapshotAsync(long expectedIndex, CancellationToken cancellation = default);
    Task<StoredResultSnapshot?> ReadStableResultAsync(Guid requestId, string owner, long expectedIndex,
        CancellationToken cancellation = default);
    Task<ReservationsSnapshot> ReadMyReservationsAsync(string owner, long expectedIndex,
        CancellationToken cancellation = default);
}
