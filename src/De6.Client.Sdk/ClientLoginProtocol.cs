using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public sealed class ClientLoginProtocol : IClientLoginProtocol
{
    public ReadOnlyMemory<byte> CreateLoginRequest(ClientLoginRequest request, Guid messageId, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DatasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(request));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", WireMessageTypes.Login);
            writer.WriteString("username", request.Username);
            writer.WriteString("password", request.Password);
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var envelope = new ClientEnvelope(
            1,
            request.DatasetId,
            messageId == Guid.Empty ? Guid.NewGuid() : messageId,
            correlationId,
            0,
            doc.RootElement.Clone());

        return WireCodec.SerializeEnvelope(envelope);
    }

    public ClientSession ParseLoginResponse(ReadOnlyMemory<byte> responseFrame, Guid expectedDatasetId, Guid expectedCorrelationId)
    {
        var envelope = WireCodec.DeserializeEnvelope(responseFrame);
        if (envelope.DatasetId != expectedDatasetId)
            throw new InvalidDataException($"Login response datasetId '{envelope.DatasetId}' does not match expected '{expectedDatasetId}'.");
        if (envelope.CorrelationId != expectedCorrelationId)
            throw new InvalidDataException($"Login response correlationId '{envelope.CorrelationId}' does not match expected '{expectedCorrelationId}'.");

        var payload = envelope.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Login response payload must be a JSON object.");

        string status = payload.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            string err = payload.TryGetProperty("error", out var e) ? e.GetString() ?? status : status;
            throw new UnauthorizedAccessException($"Login rejected: {err}");
        }

        string sessionToken = payload.GetProperty("sessionToken").GetString()!;
        string ownerId = payload.GetProperty("ownerId").GetString()!;
        DateTimeOffset expiresAt = payload.GetProperty("expiresAt").GetDateTimeOffset();
        DateTimeOffset authenticatedAt = payload.TryGetProperty("authenticatedAt", out var a)
            ? a.GetDateTimeOffset()
            : DateTimeOffset.UtcNow;

        return new ClientSession(ownerId, expectedDatasetId, sessionToken, authenticatedAt, expiresAt);
    }
}
