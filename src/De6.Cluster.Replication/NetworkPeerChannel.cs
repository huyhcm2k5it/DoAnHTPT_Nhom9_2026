using System.Text.Json;
using De6.Contracts;
using De6.Transport.Tcp;

namespace De6.Cluster.Replication;

/// <summary>
/// Network-backed implementation of IPeerChannel over framed length-prefixed TLS/TCP streams (G2).
/// Encapsulates wire envelope framing, canonical serialization of PersistedEvent,
/// correlation matching, target node verification, and AppliedReceipt decoding.
/// </summary>
public sealed class NetworkPeerChannel : IPeerChannel, IAsyncDisposable
{
    private readonly int _targetNodeId;
    private readonly int _localNodeId;
    private readonly Guid _datasetId;
    private readonly IFrameChannel _channel;

    public NetworkPeerChannel(int targetNodeId, int localNodeId, Guid datasetId, IFrameChannel channel)
    {
        if (targetNodeId is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(targetNodeId), "TargetNodeId must be 1..3.");
        if (targetNodeId == localNodeId)
            throw new ArgumentException("TargetNodeId must be distinct from local NodeId.", nameof(targetNodeId));
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));

        _targetNodeId = targetNodeId;
        _localNodeId = localNodeId;
        _datasetId = datasetId;
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public int TargetNodeId => _targetNodeId;

    public async Task<AppliedReceipt?> SendAppendApplyAsync(
        PersistedEvent persistedEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persistedEvent);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var correlationId = Guid.NewGuid();
            var payloadJson = WireCodec.CreateAppendApplyPayloadJson(persistedEvent.Epoch, persistedEvent);
            using var doc = JsonDocument.Parse(payloadJson);

            var envelope = new ClientEnvelope(
                ProtocolVersion: 1,
                DatasetId: _datasetId,
                MessageId: Guid.NewGuid(),
                CorrelationId: correlationId,
                SenderId: _localNodeId,
                Payload: doc.RootElement.Clone(),
                RequestId: null);

            var frameBytes = WireCodec.SerializeEnvelope(envelope);
            await _channel.WriteFrameAsync(frameBytes, cts.Token).ConfigureAwait(false);

            var replyFrame = await _channel.ReadFrameAsync(cts.Token).ConfigureAwait(false);
            var replyEnv = WireCodec.DeserializeEnvelope(replyFrame);

            if (replyEnv.ProtocolVersion != 1 ||
                replyEnv.DatasetId != _datasetId ||
                replyEnv.CorrelationId != correlationId ||
                replyEnv.SenderId != _targetNodeId)
            {
                return null;
            }

            var root = replyEnv.Payload;
            string status = root.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? "" : "";
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("receipt", out var receiptProp))
            {
                return null;
            }

            var receipt = WireCodec.DecodeAppliedReceipt(receiptProp);
            return receipt;
        }
        catch (Exception)
        {
            // Network fault, timeout, or frame truncation returns null to indicate no durable ACK
            return null;
        }
    }

    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
