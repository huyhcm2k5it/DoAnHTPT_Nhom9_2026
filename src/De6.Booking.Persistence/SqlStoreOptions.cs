using De6.Contracts;

namespace De6.Booking.Persistence;

public sealed record SqlStoreOptions(string ConnectionString, Guid DatasetId, int NodeId);
public enum StoreRejection { DatasetMismatch, StaleAuthority, IndexMismatch, Gap, PrefixConflict, RequestConflict, InvalidEvent, InvariantViolation, NotReady }
public sealed class StoreRejectedException(StoreRejection code, long? expectedIndex = null)
    : Exception($"Store rejected: {code}")
{
    public StoreRejection Code { get; } = code;
    public long? ExpectedIndex { get; } = expectedIndex;
}
public sealed class StorageUncertainException(Guid? requestId, Exception inner)
    : Exception("Storage operation failed or its commit outcome is uncertain; gate closed, inspect durable log before continuing", inner)
{
    public Guid? RequestId { get; } = requestId;
}

/// <summary>Optional diagnostic/fault-injection boundary. Never receives a SQL transaction or changes event bytes.</summary>
public interface IApplyObserver
{
    Task BeforeCommitAsync(PersistedEvent entry);
    Task AfterCommitAsync(PersistedEvent entry);
}
