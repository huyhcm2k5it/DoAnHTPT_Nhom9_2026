using De6.Contracts;

namespace De6.Client.Sdk;

public enum CommandOutcomeKind
{
    /// <summary>Authoritative FINAL reply. Only this kind may be presented as a business success/failure result.</summary>
    Final,
    /// <summary>UNKNOWN, RETRY_LATER, CLUSTER_NOT_READY, NOT_FOUND or a lost connection. The pending record is kept.</summary>
    Unresolved,
    /// <summary>The cluster rejected the frame before accepting it as a command.</summary>
    Rejected
}

public sealed record CommandOutcome(
    Guid RequestId,
    CommandOutcomeKind Kind,
    BusinessResult? Result,
    TransportStatus? Status,
    string? Detail);

/// <summary>
/// What the UI controller needs from the client SDK. Production code uses <see cref="SdkBookingGateway"/>;
/// tests and the demo use the same gateway on top of a simulated node connection.
/// </summary>
public interface IBookingGateway : IAsyncDisposable
{
    ClientNodeStatus? ActiveStatus { get; }
    Task<StableSnapshot> ReadSeatsAsync(CancellationToken cancellationToken);
    Task<ReservationsSnapshot> ReadMyReservationsAsync(CancellationToken cancellationToken);
    Task<CommandOutcome> ExecuteAsync(CommandIdentity identity, ReadOnlyMemory<byte> canonicalCommand, CancellationToken cancellationToken);
    Task<IReadOnlyList<PendingCommand>> ReadPendingAsync(CancellationToken cancellationToken);
    Task<CommandOutcome> ResolveAsync(Guid requestId, CancellationToken cancellationToken);
}

public sealed class ClientReadNotReadyException : Exception
{
    public TransportStatus Status { get; }
    public ClientReadNotReadyException(TransportStatus status)
        : base($"The cluster answered {status}; no authoritative snapshot is available.") => Status = status;
}
