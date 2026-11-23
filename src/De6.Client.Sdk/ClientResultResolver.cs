using System.Text;
using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public sealed class ClientResultResolver
{
    private readonly IClientPendingStore _pendingStore;
    private readonly ClientRouteManager _routeManager;
    private readonly Guid _datasetId;
    private readonly string _ownerId;

    public ClientResultResolver(
        IClientPendingStore pendingStore,
        ClientRouteManager routeManager,
        Guid datasetId,
        string ownerId)
    {
        ArgumentNullException.ThrowIfNull(pendingStore);
        ArgumentNullException.ThrowIfNull(routeManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));

        _pendingStore = pendingStore;
        _routeManager = routeManager;
        _datasetId = datasetId;
        _ownerId = ownerId;
    }

    public async Task<ClientResultResolution> ResolveAsync(
        PendingCommand pending,
        CancellationToken cancellationToken = default)
    {
        await ValidatePendingAsync(pending, cancellationToken).ConfigureAwait(false);

        ClientEnvelope lookup;
        try
        {
            lookup = await SendGetResultAsync(pending.Identity.RequestId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUncertain(exception))
        {
            return ClientResultResolution.Unresolved(exception);
        }

        ClientReplyPayload lookupReply = ClientReplyPayload.Parse(lookup.Payload);
        if (lookupReply.Status == TransportStatus.Final)
            return await PersistFinalAsync(pending.Identity.RequestId, lookup, lookupReply, false).ConfigureAwait(false);

        if (lookupReply.Status != TransportStatus.NotFound)
            return ClientResultResolution.Unresolved(null, lookup, lookupReply.Status);

        ClientEnvelope retry;
        try
        {
            using JsonDocument commandDocument = JsonDocument.Parse(pending.CanonicalCommand);
            JsonElement commandPayload = commandDocument.RootElement.Clone();
            retry = await _routeManager.SendAsync(
                new ClientRequest(pending.Identity.RequestId, commandPayload), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUncertain(exception))
        {
            return ClientResultResolution.Unresolved(exception, lookup, TransportStatus.Unknown, retried: true);
        }

        ClientReplyPayload retryReply = ClientReplyPayload.Parse(retry.Payload);
        if (retryReply.Status == TransportStatus.Final)
            return await PersistFinalAsync(pending.Identity.RequestId, retry, retryReply, true).ConfigureAwait(false);

        return ClientResultResolution.Unresolved(null, retry, retryReply.Status, retried: true);
    }

    public async Task<IReadOnlyList<ClientResultResolution>> ResolveAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingCommand> pending = await _pendingStore.ReadUnresolvedCommandsAsync(cancellationToken).ConfigureAwait(false);
        List<ClientResultResolution> results = new(pending.Count);
        foreach (PendingCommand command in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ResolveAsync(command, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    private async Task<ClientEnvelope> SendGetResultAsync(Guid requestId, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "GET_RESULT" });
        using JsonDocument document = JsonDocument.Parse(bytes);
        return await _routeManager.SendAsync(
            new ClientRequest(requestId, document.RootElement.Clone()), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientResultResolution> PersistFinalAsync(
        Guid requestId,
        ClientEnvelope response,
        ClientReplyPayload reply,
        bool retried)
    {
        if (reply.ResultPayload is null || reply.ResultPayload.Value.IsEmpty)
            throw new InvalidDataException("A FINAL response must contain a non-empty ResultPayload.");
        ReadOnlyMemory<byte> result = reply.ResultPayload.Value;

        await _pendingStore.SaveResolvedAsync(requestId, result, CancellationToken.None).ConfigureAwait(false);
        return new ClientResultResolution(TransportStatus.Final, result, response, retried, null);
    }

    private async Task ValidatePendingAsync(PendingCommand pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);
        CommandIdentity identity = pending.Identity ?? throw new InvalidDataException("Pending identity is missing.");
        if (identity.DatasetId != _datasetId || !StringComparer.Ordinal.Equals(identity.OwnerId, _ownerId))
            throw new InvalidOperationException("Pending command does not belong to the authenticated client session.");
        if (identity.RequestId == Guid.Empty || pending.CanonicalCommand.IsEmpty)
            throw new InvalidDataException("Pending RequestId or canonical command is invalid.");

        string expectedHash = CanonicalProtocol.ComputeCommandHashString(
            identity.DatasetId, identity.OwnerId, identity.Command.Action, identity.Command.TripId,
            identity.Command.SeatNo, identity.Command.ReservationId);
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedHash, identity.CommandHash))
            throw new InvalidDataException("Pending CommandHash does not match its identity.");

        using JsonDocument document = JsonDocument.Parse(pending.CanonicalCommand);
        byte[] roundTrip = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
        if (!roundTrip.AsSpan().SequenceEqual(pending.CanonicalCommand.Span))
            throw new InvalidDataException("Pending command is not canonical JSON.");

        IReadOnlyList<PendingCommand> persisted = await _pendingStore.ReadUnresolvedCommandsAsync(cancellationToken).ConfigureAwait(false);
        PendingCommand? stored = persisted.FirstOrDefault(x => x.Identity.RequestId == identity.RequestId);
        if (stored is null || stored.Identity != identity ||
            !stored.CanonicalCommand.Span.SequenceEqual(pending.CanonicalCommand.Span))
            throw new InvalidOperationException("Resolver input does not match the durable pending command.");
    }

    private static bool IsUncertain(Exception exception) =>
        exception is ClientTransportUncertainException or StaleClientResponseException or ClientRouteException or IOException;

}

public sealed record ClientResultResolution(
    TransportStatus Status,
    ReadOnlyMemory<byte>? ResultPayload,
    ClientEnvelope? Response,
    bool Retried,
    Exception? TransportError)
{
    public bool IsFinal => Status == TransportStatus.Final && ResultPayload is { IsEmpty: false };

    internal static ClientResultResolution Unresolved(
        Exception? error,
        ClientEnvelope? response = null,
        TransportStatus status = TransportStatus.Unknown,
        bool retried = false) => new(status, null, response, retried, error);
}

internal sealed record ClientReplyPayload(TransportStatus Status, ReadOnlyMemory<byte>? ResultPayload)
{
    public static ClientReplyPayload Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Booking response payload must be a JSON object.");

        if (!TryGetProperty(payload, "status", out JsonElement statusElement))
            throw new InvalidDataException("Booking response does not contain status.");

        TransportStatus status;
        if (statusElement.ValueKind == JsonValueKind.String)
        {
            string? value = statusElement.GetString();
            // Wire protocol statuses use names such as NOT_FOUND and RETRY_LATER,
            // while the C# enum uses NotFound and RetryLater.
            string normalized = (value ?? string.Empty)
                .Trim()
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            if (!Enum.TryParse(normalized, ignoreCase: true, out status) || !Enum.IsDefined(status))
                throw new InvalidDataException($"Unknown transport status '{value}'.");
        }
        else if (statusElement.ValueKind == JsonValueKind.Number && statusElement.TryGetInt32(out int numeric) && Enum.IsDefined(typeof(TransportStatus), numeric))
        {
            status = (TransportStatus)numeric;
        }
        else
        {
            throw new InvalidDataException("Booking response status is invalid.");
        }

        ReadOnlyMemory<byte>? result = null;
        if (TryGetProperty(payload, "result", out JsonElement resultElement) && resultElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            result = Encoding.UTF8.GetBytes(resultElement.GetRawText());

        if (status == TransportStatus.Final && result is null)
            throw new InvalidDataException("FINAL response is missing result payload.");
        if (status != TransportStatus.Final && result is not null)
            throw new InvalidDataException("Only FINAL may contain a business result payload.");

        return new ClientReplyPayload(status, result);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
