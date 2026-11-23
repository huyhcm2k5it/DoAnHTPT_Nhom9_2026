using System.Text.Json;
using De6.Booking.Persistence;
using De6.Cluster.Replication;
using De6.Contracts;
using De6.Transport.Tcp;

namespace De6.Server.Host;

/// <summary>
/// Server-side dispatcher for peer replication and lifecycle connections (G2/G3).
/// Receives framed wire envelopes on the peer TLS port, authenticates the peer identity,
/// validates dataset and authority, forwards APPEND_APPLY to ReplicaReceiver,
/// and responds with durable ACK AppliedReceipt envelopes.
/// </summary>
public sealed class PeerRequestDispatcher
{
    private readonly int _localNodeId;
    private readonly Guid _datasetId;
    private readonly ReplicaReceiver _receiver;

    public PeerRequestDispatcher(int localNodeId, Guid datasetId, ReplicaReceiver receiver)
    {
        if (localNodeId is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(localNodeId), "Local NodeId must be 1..3.");
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));

        _localNodeId = localNodeId;
        _datasetId = datasetId;
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
    }

    public async Task HandleConnectionAsync(
        IFrameChannel channel,
        AuthenticatedPeerIdentity peerIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(peerIdentity);

        if (peerIdentity.NodeId is < 1 or > 3 || peerIdentity.NodeId == _localNodeId)
            throw new UnauthorizedAccessException("Peer identity is invalid or matches local node.");

        while (!cancellationToken.IsCancellationRequested)
        {
            ReadOnlyMemory<byte> frame;
            try
            {
                frame = await channel.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            ClientEnvelope request;
            try
            {
                request = WireCodec.DeserializeEnvelope(frame);
            }
            catch (Exception)
            {
                break;
            }

            if (request.DatasetId != _datasetId)
            {
                var errJson = WireCodec.CreateAckAppliedPayloadJson("REJECTED", error: "DATASET_MISMATCH");
                var errEnv = CreateReplyEnvelope(request, errJson);
                await channel.WriteFrameAsync(WireCodec.SerializeEnvelope(errEnv), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (request.SenderId != peerIdentity.NodeId)
            {
                var errJson = WireCodec.CreateAckAppliedPayloadJson("REJECTED", error: "PEER_IDENTITY_MISMATCH");
                var errEnv = CreateReplyEnvelope(request, errJson);
                await channel.WriteFrameAsync(WireCodec.SerializeEnvelope(errEnv), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var replyPayloadJson = await DispatchPeerRequestAsync(request, peerIdentity, cancellationToken).ConfigureAwait(false);
            var replyEnv = CreateReplyEnvelope(request, replyPayloadJson);
            await channel.WriteFrameAsync(WireCodec.SerializeEnvelope(replyEnv), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string> DispatchPeerRequestAsync(
        ClientEnvelope request,
        AuthenticatedPeerIdentity peerIdentity,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        string type = "";
        if (payload.TryGetProperty("type", out var tProp) || payload.TryGetProperty("action", out tProp))
        {
            type = tProp.GetString()?.ToUpperInvariant() ?? "";
        }

        // Rejection of Client RPCs on peer port per V12
        if (type is WireMessageTypes.Login or WireMessageTypes.GetSeats or WireMessageTypes.GetMyReservations
            or WireMessageTypes.GetResult or WireMessageTypes.Hold or WireMessageTypes.Confirm or WireMessageTypes.Cancel)
        {
            return "{\"status\":\"REJECTED\",\"error\":\"CLIENT_RPC_NOT_ALLOWED_ON_PEER_PORT\"}";
        }

        if (type == WireMessageTypes.AppendApply)
        {
            try
            {
                long authorityEpoch = payload.GetProperty("authorityEpoch").GetInt64();
                var eventElement = payload.GetProperty("event");
                var entry = WireCodec.DecodePersistedEvent(eventElement);

                var receipt = await _receiver.AppendAsync(
                    request.ProtocolVersion,
                    peerIdentity,
                    authorityEpoch,
                    entry,
                    cancellationToken).ConfigureAwait(false);

                return WireCodec.CreateAckAppliedPayloadJson("OK", receipt);
            }
            catch (StoreRejectedException ex)
            {
                var status = ex.Code switch
                {
                    StoreRejection.Gap => "GAP",
                    StoreRejection.PrefixConflict => "CONFLICT",
                    _ => "REJECTED"
                };
                return WireCodec.CreateAckAppliedPayloadJson(status, error: ex.Code.ToString(), expectedIndex: ex.ExpectedIndex);
            }
            catch (UnauthorizedAccessException)
            {
                return WireCodec.CreateAckAppliedPayloadJson("REJECTED", error: "STALE_AUTHORITY");
            }
            catch (InvalidDataException)
            {
                return WireCodec.CreateAckAppliedPayloadJson("REJECTED", error: "INVALID_EVENT");
            }
            catch (StorageUncertainException)
            {
                return WireCodec.CreateAckAppliedPayloadJson("ERROR", error: "STORAGE_UNCERTAIN");
            }
        }

        return "{\"status\":\"REJECTED\",\"error\":\"UNKNOWN_PEER_MESSAGE_TYPE\"}";
    }

    private ClientEnvelope CreateReplyEnvelope(ClientEnvelope request, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new ClientEnvelope(
            ProtocolVersion: 1,
            DatasetId: _datasetId,
            MessageId: Guid.NewGuid(),
            CorrelationId: request.CorrelationId,
            SenderId: _localNodeId,
            Payload: doc.RootElement.Clone(),
            RequestId: request.RequestId);
    }
}
