namespace De6.Contracts;

// Shared shapes only. Canonical bytes, protocol validation and interoperability fixtures
// remain implementation work under V12 sections 8, 15 and 16.
public enum BookingAction { Hold, Confirm, Cancel }
public enum SeatStatus { Available, Held, Sold }
public enum TransportStatus { ClusterNotReady, Unknown, Final }
public sealed record CommandData(BookingAction Action, int TripId, int SeatNo, Guid? ReservationId);
public sealed record CommandIdentity(Guid DatasetId, Guid RequestId, string OwnerId, string CommandHash, CommandData Command);
public sealed record AuthenticatedCommand(CommandIdentity Identity);
public sealed record AcceptedAuthority(long Epoch, int LeaderId);
public sealed record BusinessResult(string Code, int TripId, int SeatNo, Guid? ReservationId, Guid? TicketId);
public sealed record CommandReply(TransportStatus Status, BusinessResult? Result);
public sealed record PersistedEvent(Guid DatasetId, long LogIndex, long Epoch, int WriterId, Guid EventId,
    ReadOnlyMemory<byte> PayloadBytes, ReadOnlyMemory<byte> EventHash, ReadOnlyMemory<byte> ResultPayload);
public sealed record AppliedReceipt(int NodeId, long AuthorityEpoch, long EventEpoch, long LogIndex,
    Guid EventId, ReadOnlyMemory<byte> EventHash, Guid? RecoveryRunId);
public sealed record RecoveryContext(Guid RunId, Guid DatasetId, int SourceNodeId, int TargetNodeId,
    long TargetEpoch, int TargetLeaderId, long BaseIndex, ReadOnlyMemory<byte> BaseHash,
    long TargetIndex, ReadOnlyMemory<byte> HeadHash);
public sealed record SeatView(int TripId, int SeatNo, SeatStatus Status, Guid? ReservationId, string? OwnerId, long VersionIndex);
public sealed record StableSnapshot(Guid DatasetId, long Epoch, long AppliedIndex, IReadOnlyList<SeatView> Seats);
