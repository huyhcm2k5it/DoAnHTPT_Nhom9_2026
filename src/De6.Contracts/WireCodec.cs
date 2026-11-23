using System.Text;
using System.Text.Json;

namespace De6.Contracts;

public static class WireCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static ReadOnlyMemory<byte> SerializeEnvelope(ClientEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.ProtocolVersion != 1)
            throw new InvalidDataException($"ProtocolVersion must be 1, got {envelope.ProtocolVersion}.");
        if (envelope.DatasetId == Guid.Empty)
            throw new InvalidDataException("DatasetId cannot be empty in wire envelope.");
        if (envelope.CorrelationId == Guid.Empty)
            throw new InvalidDataException("CorrelationId cannot be empty in wire envelope.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", envelope.ProtocolVersion);
            writer.WriteString("datasetId", envelope.DatasetId);
            writer.WriteString("messageId", envelope.MessageId == Guid.Empty ? Guid.NewGuid() : envelope.MessageId);
            writer.WriteString("correlationId", envelope.CorrelationId);
            writer.WriteNumber("senderId", envelope.SenderId);
            if (envelope.RequestId.HasValue)
            {
                writer.WriteString("requestId", envelope.RequestId.Value);
            }
            writer.WritePropertyName("payload");
            envelope.Payload.WriteTo(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static ClientEnvelope DeserializeEnvelope(ReadOnlyMemory<byte> utf8Bytes)
    {
        if (utf8Bytes.IsEmpty)
            throw new InvalidDataException("Wire envelope frame cannot be empty.");

        using var document = JsonDocument.Parse(utf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Wire envelope must be a JSON object.");

        int protocolVersion = 0;
        if (root.TryGetProperty("protocolVersion", out var pvProp) || root.TryGetProperty("ProtocolVersion", out pvProp))
        {
            protocolVersion = pvProp.GetInt32();
        }
        if (protocolVersion != 1)
            throw new InvalidDataException($"Unsupported ProtocolVersion '{protocolVersion}'. Must be 1.");

        Guid datasetId = Guid.Empty;
        if (root.TryGetProperty("datasetId", out var dsProp) || root.TryGetProperty("DatasetId", out dsProp))
        {
            datasetId = dsProp.GetGuid();
        }
        if (datasetId == Guid.Empty)
            throw new InvalidDataException("DatasetId cannot be empty.");

        Guid messageId = Guid.Empty;
        if (root.TryGetProperty("messageId", out var msgProp) || root.TryGetProperty("MessageId", out msgProp))
        {
            messageId = msgProp.GetGuid();
        }

        Guid correlationId = Guid.Empty;
        if (root.TryGetProperty("correlationId", out var corrProp) || root.TryGetProperty("CorrelationId", out corrProp))
        {
            correlationId = corrProp.GetGuid();
        }
        if (correlationId == Guid.Empty)
            throw new InvalidDataException("CorrelationId cannot be empty.");

        int senderId = 0;
        if (root.TryGetProperty("senderId", out var sProp) || root.TryGetProperty("SenderId", out sProp))
        {
            senderId = sProp.GetInt32();
        }

        Guid? requestId = null;
        if ((root.TryGetProperty("requestId", out var reqProp) || root.TryGetProperty("RequestId", out reqProp)) &&
            reqProp.ValueKind == JsonValueKind.String && reqProp.TryGetGuid(out var reqGuid))
        {
            requestId = reqGuid;
        }

        if (!root.TryGetProperty("payload", out var payloadProp) && !root.TryGetProperty("Payload", out payloadProp))
            throw new InvalidDataException("Wire envelope must contain a 'payload' property.");

        return new ClientEnvelope(
            protocolVersion,
            datasetId,
            messageId,
            correlationId,
            senderId,
            payloadProp.Clone(),
            requestId);
    }

    public static ReadOnlyMemory<byte> EncodePersistedEvent(PersistedEvent persistedEvent)
    {
        ArgumentNullException.ThrowIfNull(persistedEvent);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("datasetId", persistedEvent.DatasetId);
            writer.WriteNumber("logIndex", persistedEvent.LogIndex);
            writer.WriteNumber("epoch", persistedEvent.Epoch);
            writer.WriteNumber("writerId", persistedEvent.WriterId);
            writer.WriteString("eventId", persistedEvent.EventId);
            writer.WriteString("eventHashHex", Convert.ToHexString(persistedEvent.EventHash.Span).ToLowerInvariant());
            writer.WriteString("payloadBase64", Convert.ToBase64String(persistedEvent.PayloadBytes.Span));
            writer.WriteString("resultPayloadBase64", Convert.ToBase64String(persistedEvent.ResultPayload.Span));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static PersistedEvent DecodePersistedEvent(ReadOnlyMemory<byte> bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        return DecodePersistedEvent(doc.RootElement);
    }

    public static PersistedEvent DecodePersistedEvent(JsonElement root)
    {
        var datasetId = root.GetProperty("datasetId").GetGuid();
        var logIndex = root.GetProperty("logIndex").GetInt64();
        var epoch = root.GetProperty("epoch").GetInt64();
        var writerId = root.GetProperty("writerId").GetInt32();
        var eventId = root.GetProperty("eventId").GetGuid();
        var eventHashHex = root.GetProperty("eventHashHex").GetString()!;
        var payloadBase64 = root.GetProperty("payloadBase64").GetString()!;
        var resultPayloadBase64 = root.GetProperty("resultPayloadBase64").GetString()!;

        byte[] payloadBytes = Convert.FromBase64String(payloadBase64);
        byte[] resultPayload = Convert.FromBase64String(resultPayloadBase64);
        byte[] eventHash = Convert.FromHexString(eventHashHex);

        return new PersistedEvent(datasetId, logIndex, epoch, writerId, eventId, payloadBytes, eventHash, resultPayload);
    }

    public static ReadOnlyMemory<byte> EncodeAppliedReceipt(AppliedReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("nodeId", receipt.NodeId);
            writer.WriteNumber("authorityEpoch", receipt.AuthorityEpoch);
            writer.WriteNumber("eventEpoch", receipt.EventEpoch);
            writer.WriteNumber("logIndex", receipt.LogIndex);
            writer.WriteString("eventId", receipt.EventId);
            writer.WriteString("eventHashHex", Convert.ToHexString(receipt.EventHash.Span).ToLowerInvariant());
            if (receipt.RecoveryRunId.HasValue)
            {
                writer.WriteString("recoveryRunId", receipt.RecoveryRunId.Value);
            }
            else
            {
                writer.WriteNull("recoveryRunId");
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static AppliedReceipt DecodeAppliedReceipt(JsonElement root)
    {
        int nodeId = root.GetProperty("nodeId").GetInt32();
        long authorityEpoch = root.GetProperty("authorityEpoch").GetInt64();
        long eventEpoch = root.GetProperty("eventEpoch").GetInt64();
        long logIndex = root.GetProperty("logIndex").GetInt64();
        Guid eventId = root.GetProperty("eventId").GetGuid();
        string eventHashHex = root.GetProperty("eventHashHex").GetString()!;
        byte[] eventHash = Convert.FromHexString(eventHashHex);

        Guid? recoveryRunId = null;
        if (root.TryGetProperty("recoveryRunId", out var rProp) &&
            rProp.ValueKind == JsonValueKind.String &&
            rProp.TryGetGuid(out var runId))
        {
            recoveryRunId = runId;
        }

        return new AppliedReceipt(nodeId, authorityEpoch, eventEpoch, logIndex, eventId, eventHash, recoveryRunId);
    }

    public static string CreateAppendApplyPayloadJson(long authorityEpoch, PersistedEvent entry)
    {
        var eventJson = Encoding.UTF8.GetString(EncodePersistedEvent(entry).Span);
        return $"{{\"type\":\"{WireMessageTypes.AppendApply}\",\"authorityEpoch\":{authorityEpoch},\"event\":{eventJson}}}";
    }

    public static string CreateAckAppliedPayloadJson(string status, AppliedReceipt? receipt = null, string? error = null,
        long? expectedIndex = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", WireMessageTypes.AckApplied);
            writer.WriteString("status", status);
            if (error is not null) writer.WriteString("error", error);
            if (expectedIndex is { } index) writer.WriteNumber("expectedIndex", index);
            if (receipt is not null)
            {
                writer.WritePropertyName("receipt");
                writer.WriteRawValue(EncodeAppliedReceipt(receipt).Span);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
