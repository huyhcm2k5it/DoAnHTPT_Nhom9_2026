using De6.Contracts;

namespace De6.Cluster.Replication;

/// <summary>
/// Abstraction representing a point-to-point replication transport channel to a specific peer node.
/// Wire implementation (TCP/TLS framed client) is wired in De6.Transport.Tcp (Văn Khoa).
/// </summary>
public interface IPeerChannel
{
    /// <summary>
    /// The configured Node ID of the target peer (2 or 3).
    /// </summary>
    int TargetNodeId { get; }

    /// <summary>
    /// Sends a PersistedEvent to the target backup node and awaits an AppliedReceipt.
    /// Returns null if peer fails, disconnects, rejects, or times out.
    /// </summary>
    Task<AppliedReceipt?> SendAppendApplyAsync(PersistedEvent persistedEvent, TimeSpan timeout, CancellationToken cancellationToken);
}
