using System.Text;
using System.Text.Json;
using De6.Booking.Domain;
using De6.Cluster.Control;
using De6.Contracts;
using De6.Transport.Tcp;

namespace De6.Server.Host;

public sealed class ClientRequestDispatcher
{
    private readonly Guid _datasetId;
    private readonly int _nodeId;
    private readonly ClusterControl _control;
    private readonly ICommandCoordinator _coordinator;
    private readonly ClientSessionManager _sessionManager;
    private readonly Func<bool> _sqlHealthy;

    public ClientRequestDispatcher(
        Guid datasetId,
        int nodeId,
        ClusterControl control,
        ICommandCoordinator coordinator,
        ClientSessionManager sessionManager,
        Func<bool>? sqlHealthy = null)
    {
        _datasetId = datasetId;
        _nodeId = nodeId;
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _sqlHealthy = sqlHealthy ?? (() => false);
    }

    public async Task HandleConnectionAsync(IFrameChannel channel, CancellationToken cancellationToken)
    {
        ClientSessionRecord? session = null;

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
                var errEnv = CreateReplyEnvelope(request, "{\"status\":\"REJECTED\",\"error\":\"DATASET_MISMATCH\"}");
                await channel.WriteFrameAsync(WireCodec.SerializeEnvelope(errEnv), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var replyPayload = await DispatchAsync(request, session, s => session = s, cancellationToken).ConfigureAwait(false);
            var replyEnv = CreateReplyEnvelope(request, replyPayload);
            await channel.WriteFrameAsync(WireCodec.SerializeEnvelope(replyEnv), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string> DispatchAsync(
        ClientEnvelope request,
        ClientSessionRecord? currentSession,
        Action<ClientSessionRecord> setSession,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
            return "{\"status\":\"REJECTED\",\"error\":\"INVALID_PAYLOAD\"}";
        string type = "";
        if (payload.TryGetProperty("type", out var tProp) || payload.TryGetProperty("action", out tProp))
        {
            if (tProp.ValueKind != JsonValueKind.String)
                return "{\"status\":\"REJECTED\",\"error\":\"INVALID_MESSAGE_TYPE\"}";
            type = tProp.GetString()?.ToUpperInvariant() ?? "";
        }

        // Rejection of Peer/Admin RPCs on client port per V12
        if (type is "APPEND_APPLY" or "REPLAY_ENTRY" or "ELECTION" or "ANSWER" or "COORDINATOR")
        {
            return "{\"status\":\"REJECTED\",\"error\":\"PEER_RPC_NOT_ALLOWED_ON_CLIENT_PORT\"}";
        }

        // 1. LOGIN
        if (type == WireMessageTypes.Login)
        {
            string username = payload.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string password = payload.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
            var newSession = _sessionManager.Authenticate(username, password, _datasetId);
            if (newSession is null)
            {
                return "{\"status\":\"REJECTED\",\"error\":\"INVALID_CREDENTIALS\"}";
            }

            setSession(newSession);
            return $"{{\"status\":\"OK\",\"sessionToken\":\"{newSession.Token}\",\"ownerId\":\"{newSession.OwnerId}\",\"authenticatedAt\":\"{newSession.AuthenticatedAt:O}\",\"expiresAt\":\"{newSession.ExpiresAt:O}\"}}";
        }

        // 2. GET_CLUSTER_STATUS (permitted before login per V12)
        if (type == WireMessageTypes.GetClusterStatus)
        {
            var snap = _control.Snapshot;
            string role = snap.Role.ToString().ToUpperInvariant();
            long epoch = snap.AcceptedAuthority?.Epoch ?? _control.CurrentEpoch;
            int leaderId = snap.AcceptedAuthority?.LeaderId ?? _control.LeaderId;
            return $"{{\"status\":\"OK\",\"nodeId\":{_nodeId},\"datasetId\":\"{_datasetId}\",\"role\":\"{role}\",\"epoch\":{epoch},\"acceptedLeaderId\":{leaderId},\"sqlHealthy\":{_sqlHealthy().ToString().ToLowerInvariant()},\"localAppliedIndex\":{snap.LastAppliedIndex},\"stableIndex\":{snap.StableIndex},\"followerLag\":0}}";
        }

        // Check authentication for all business operations
        if (currentSession is null || currentSession.IsExpired())
        {
            return "{\"status\":\"REJECTED\",\"error\":\"UNAUTHORIZED\"}";
        }

        // 3. GET_SEATS
        if (type == WireMessageTypes.GetSeats)
        {
            try
            {
                var seats = await _coordinator.GetSeatsAsync(cancellationToken).ConfigureAwait(false);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("status", "FINAL");
                    writer.WriteString("datasetId", seats.DatasetId);
                    writer.WriteNumber("epoch", seats.Epoch);
                    writer.WriteNumber("appliedIndex", seats.AppliedIndex);
                    writer.WriteStartArray("seats");
                    foreach (var s in seats.Seats)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("tripId", s.TripId);
                        writer.WriteNumber("seatNo", s.SeatNo);
                        writer.WriteString("status", s.Status.ToString().ToUpperInvariant());
                        if (s.ReservationId.HasValue) writer.WriteString("reservationId", s.ReservationId.Value);
                        else writer.WriteNull("reservationId");
                        if (s.OwnerId is not null) writer.WriteString("ownerId", s.OwnerId);
                        else writer.WriteNull("ownerId");
                        writer.WriteNumber("versionIndex", s.VersionIndex);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (InvalidOperationException)
            {
                return "{\"status\":\"CLUSTER_NOT_READY\"}";
            }
        }

        // 4. GET_MY_RESERVATIONS (Filtered by currentSession.OwnerId per V12)
        if (type == WireMessageTypes.GetMyReservations)
        {
            try
            {
                var reservations = await _coordinator.GetMyReservationsAsync(currentSession.OwnerId, cancellationToken).ConfigureAwait(false);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("status", "FINAL");
                    writer.WriteString("datasetId", reservations.DatasetId);
                    writer.WriteNumber("epoch", reservations.Epoch);
                    writer.WriteNumber("appliedIndex", reservations.AppliedIndex);
                    writer.WriteStartArray("reservations");
                    foreach (var r in reservations.Reservations)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("reservationId", r.ReservationId);
                        writer.WriteNumber("tripId", r.TripId);
                        writer.WriteNumber("seatNo", r.SeatNo);
                        writer.WriteString("ownerId", r.OwnerId);
                        writer.WriteString("status", r.Status.ToString().ToUpperInvariant());
                        writer.WriteString("createdUtc", r.CreatedUtc.ToString("O"));
                        writer.WriteNumber("updatedIndex", r.UpdatedIndex);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (InvalidOperationException)
            {
                return "{\"status\":\"CLUSTER_NOT_READY\"}";
            }
        }

        // 5. GET_RESULT
        if (type == WireMessageTypes.GetResult)
        {
            Guid reqId = request.RequestId ?? (payload.TryGetProperty("requestId", out var r) ? r.GetGuid() : Guid.Empty);
            if (reqId == Guid.Empty)
                return "{\"status\":\"REJECTED\",\"error\":\"MISSING_REQUEST_ID\"}";

            var reply = await _coordinator.GetResultAsync(reqId, currentSession.OwnerId, cancellationToken).ConfigureAwait(false);
            return FormatCommandReply(reply);
        }

        // 6. HOLD, CONFIRM, CANCEL
        if (type is "HOLD" or "CONFIRM" or "CANCEL")
        {
            if (request.RequestId is not { } reqId || reqId == Guid.Empty)
                return "{\"status\":\"REJECTED\",\"error\":\"MISSING_REQUEST_ID\"}";

            BookingAction action = type switch
            {
                "HOLD" => BookingAction.Hold,
                "CONFIRM" => BookingAction.Confirm,
                "CANCEL" => BookingAction.Cancel,
                _ => throw new InvalidOperationException()
            };

            if (!payload.TryGetProperty("tripId", out var tr) || tr.ValueKind != JsonValueKind.Number || !tr.TryGetInt32(out var tripId) ||
                !payload.TryGetProperty("seatNo", out var st) || st.ValueKind != JsonValueKind.Number || !st.TryGetInt32(out var seatNo))
                return "{\"status\":\"REJECTED\",\"error\":\"INVALID_COMMAND_FIELDS\"}";
            Guid? resId = null;
            if (payload.TryGetProperty("reservationId", out var rProp) && rProp.ValueKind == JsonValueKind.String && rProp.TryGetGuid(out var rg))
                resId = rg;

            var cmdData = new CommandData(action, tripId, seatNo, resId);
            string hash = BookingCanonical.CommandHash(_datasetId, currentSession.OwnerId, cmdData);
            var identity = new CommandIdentity(_datasetId, reqId, currentSession.OwnerId, hash, cmdData);
            var authCmd = new AuthenticatedCommand(identity);

            var reply = await _coordinator.SubmitAsync(authCmd, cancellationToken).ConfigureAwait(false);
            return FormatCommandReply(reply);
        }

        return "{\"status\":\"REJECTED\",\"error\":\"UNKNOWN_MESSAGE_TYPE\"}";
    }

    private static string FormatCommandReply(CommandReply reply)
    {
        string statusStr = reply.Status switch
        {
            TransportStatus.Final => "FINAL",
            TransportStatus.Unknown => "UNKNOWN",
            TransportStatus.RetryLater => "RETRY_LATER",
            TransportStatus.ClusterNotReady => "CLUSTER_NOT_READY",
            TransportStatus.NotFound => "NOT_FOUND",
            TransportStatus.Rejected => "REJECTED",
            _ => reply.Status.ToString().ToUpperInvariant()
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("status", statusStr);
            if (reply.AppliedIndex is { } index) writer.WriteNumber("appliedIndex", index);
            if (reply.ErrorCode is { } error) writer.WriteString("error", error);
            if (reply.Status == TransportStatus.Final && reply.Result is { } result)
            {
                writer.WritePropertyName("result");
                writer.WriteRawValue(CanonicalProtocol.SerializeBusinessResult(result));
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private ClientEnvelope CreateReplyEnvelope(ClientEnvelope request, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new ClientEnvelope(
            1,
            _datasetId,
            Guid.NewGuid(),
            request.CorrelationId,
            _nodeId,
            doc.RootElement.Clone(),
            request.RequestId);
    }
}
