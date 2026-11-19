namespace De6.Contracts;

public enum BookingAction { Hold, Confirm, Cancel }
public enum SeatStatus { Available, Held, Sold }
public enum ReservationStatus { Held, Confirmed, Cancelled }
public enum DecisionKind { HeldCreated, SoldConfirmed, HoldCancelled, NoChange }
public enum TransportStatus { ClusterNotReady, Unknown, Final, RetryLater, Rejected, NotFound }

public static class ResultCodes
{
    public const string OkHeld = "OK_HELD";
    public const string OkSold = "OK_SOLD";
    public const string OkCancelled = "OK_CANCELLED";
    public const string SeatBusy = "SEAT_BUSY";
    public const string AlreadySold = "ALREADY_SOLD";
    public const string AlreadyConfirmed = "ALREADY_CONFIRMED";
    public const string ReservationCancelled = "RESERVATION_CANCELLED";
    public const string AlreadyCancelled = "ALREADY_CANCELLED";
    public const string TicketNotCancellable = "TICKET_NOT_CANCELLABLE";
    public const string NotOwner = "NOT_OWNER";
    public const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    public const string ReservationSeatMismatch = "RESERVATION_SEAT_MISMATCH";
    public const string RequestConflict = "REQUEST_CONFLICT";
    public const string RetryLater = "RETRY_LATER";
    public const string ClusterNotReady = "CLUSTER_NOT_READY";
}

public sealed record CommandData(BookingAction Action, int TripId, int SeatNo, Guid? ReservationId);
public sealed record CommandIdentity(Guid DatasetId, Guid RequestId, string OwnerId, string CommandHash, CommandData Command);
public sealed record AuthenticatedCommand(CommandIdentity Identity);
public sealed record AcceptedAuthority(long Epoch, int LeaderId);

// BusinessResult v1: code, tripId, seatNo, reservationId, ticketId (fixed order, null for unused)
public sealed record BusinessResult(string Code, int TripId, int SeatNo, Guid? ReservationId, Guid? TicketId);

public sealed record ReservationRecord(Guid ReservationId, int TripId, int SeatNo, string OwnerId, ReservationStatus Status, DateTime CreatedUtc, long UpdatedIndex);
public sealed record TicketRecord(Guid TicketId, int TripId, int SeatNo, Guid ReservationId, string OwnerId, DateTime IssuedUtc, long IssuedIndex);

public sealed record BookingDecision(DecisionKind Kind, SeatView? SeatAfter, ReservationRecord? ReservationAfter, TicketRecord? TicketCreated, BusinessResult Result);

public sealed record CommandReply(TransportStatus Status, BusinessResult? Result, string? ErrorCode = null);

public sealed record PersistedEvent(Guid DatasetId, long LogIndex, long Epoch, int WriterId, Guid EventId,
    ReadOnlyMemory<byte> PayloadBytes, ReadOnlyMemory<byte> EventHash, ReadOnlyMemory<byte> ResultPayload);

public sealed record AppliedReceipt(int NodeId, long AuthorityEpoch, long EventEpoch, long LogIndex,
    Guid EventId, ReadOnlyMemory<byte> EventHash, Guid? RecoveryRunId);

public sealed record RecoveryContext(Guid RunId, Guid DatasetId, int SourceNodeId, int TargetNodeId,
    long TargetEpoch, int TargetLeaderId, long BaseIndex, ReadOnlyMemory<byte> BaseHash,
    long TargetIndex, ReadOnlyMemory<byte> HeadHash);

public sealed record SeatView(int TripId, int SeatNo, SeatStatus Status, Guid? ReservationId, string? OwnerId, long VersionIndex);
public sealed record StableSnapshot(Guid DatasetId, long Epoch, long AppliedIndex, IReadOnlyList<SeatView> Seats);
public sealed record StableReservationsSnapshot(Guid DatasetId, long Epoch, long AppliedIndex, IReadOnlyList<ReservationRecord> Reservations);

public sealed record EventPayloadData(
    Guid DatasetId,
    long LogIndex,
    long Epoch,
    int WriterId,
    Guid EventId,
    string PreviousHashHex,
    Guid RequestId,
    string OwnerId,
    string CommandHash,
    CommandData Command,
    BookingDecision Decision,
    long BeforeSeatVersion,
    BusinessResult Result,
    DateTime CreatedUtc);
