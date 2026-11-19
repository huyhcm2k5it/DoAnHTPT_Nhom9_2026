using De6.Contracts;

namespace De6.Cluster.Replication;

/// <summary>Peer dispatch after TLS authentication. Storage owns leases, prefix checks and commit.</summary>
public sealed class ReplicaReceiver(Guid datasetId, int nodeId, IBookingStore store)
{
    public Task<AppliedReceipt> AppendAsync(int protocolVersion, AuthenticatedPeerIdentity peer,
        long authorityEpoch, PersistedEvent entry, CancellationToken cancellationToken = default)
    {
        ValidatePeer(protocolVersion, peer, entry);
        if (authorityEpoch <= 0 || entry.Epoch != authorityEpoch || entry.WriterId != peer.NodeId)
            throw new UnauthorizedAccessException("Append does not match the authenticated writer and authority epoch.");

        return store.AppendAndApplyAsync(entry, new(peer.NodeId, new(authorityEpoch, peer.NodeId)), cancellationToken);
    }

    // The dispatcher supplies the local run context. The storage gate independently
    // verifies that exact context is installed and has not expired.
    public Task<AppliedReceipt> ReplayAsync(int protocolVersion, AuthenticatedPeerIdentity peer,
        RecoveryContext localContext, PersistedEvent entry, CancellationToken cancellationToken = default)
    {
        ValidatePeer(protocolVersion, peer, entry);
        ArgumentNullException.ThrowIfNull(localContext);
        if (localContext.DatasetId != datasetId || localContext.TargetNodeId != nodeId ||
            localContext.SourceNodeId != peer.NodeId)
            throw new UnauthorizedAccessException("Replay does not match the authenticated source and local recovery run.");

        return store.ApplyReplayEntryAsync(entry, peer, localContext, cancellationToken);
    }

    private void ValidatePeer(int protocolVersion, AuthenticatedPeerIdentity peer, PersistedEvent entry)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(entry);
        if (protocolVersion != 1 || entry.DatasetId != datasetId)
            throw new InvalidDataException("Unsupported replication protocol or dataset.");
        if (peer.NodeId is < 1 or > 3 || peer.NodeId == nodeId)
            throw new UnauthorizedAccessException("Replication requires a distinct authenticated peer.");
    }
}
