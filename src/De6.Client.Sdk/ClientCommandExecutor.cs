using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public sealed class ClientCommandExecutor
{
    private readonly IClientPendingStore _pendingStore;
    private readonly ClientRouteManager _routeManager;
    private readonly string _ownerId;
    private readonly Guid _datasetId;
    private readonly ClientResultResolver _resultResolver;

    public ClientCommandExecutor(IClientPendingStore pendingStore, ClientRouteManager routeManager, Guid datasetId, string ownerId)
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
        _resultResolver = new ClientResultResolver(pendingStore, routeManager, datasetId, ownerId);
    }

    public async Task<ClientEnvelope> ExecuteAsync(
        CommandIdentity identity,
        ReadOnlyMemory<byte> canonicalCommand,
        Func<ClientEnvelope, ReadOnlyMemory<byte>?> finalResultSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(finalResultSelector);
        ValidateIdentity(identity);
        if (canonicalCommand.IsEmpty)
            throw new ArgumentException("Canonical command cannot be empty.", nameof(canonicalCommand));

        using JsonDocument document = JsonDocument.Parse(canonicalCommand);
        JsonElement payload = document.RootElement.Clone();
        byte[] roundTrip = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (!roundTrip.AsSpan().SequenceEqual(canonicalCommand.Span))
            throw new InvalidDataException("Canonical command bytes must match the JSON payload bytes exactly.");

        await _pendingStore.SaveBeforeSendAsync(identity, canonicalCommand, cancellationToken).ConfigureAwait(false);
        ClientEnvelope response;
        try
        {
            response = await _routeManager.SendAsync(new ClientRequest(identity.RequestId, payload), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ClientTransportUncertainException or StaleClientResponseException or ClientRouteException)
        {
            ClientResultResolution resolution = await ResolvePendingAsync(identity.RequestId, cancellationToken).ConfigureAwait(false);
            if (resolution.IsFinal && resolution.Response is not null)
                return resolution.Response;
            throw;
        }

        ClientReplyPayload reply = ClientReplyPayload.Parse(response.Payload);
        if (reply.Status == TransportStatus.Final)
        {
            ReadOnlyMemory<byte>? selected = finalResultSelector(response) ?? reply.ResultPayload;
            if (selected is null || selected.Value.IsEmpty)
                throw new InvalidDataException("FINAL response did not provide a durable result payload.");
            await _pendingStore.SaveResolvedAsync(identity.RequestId, selected.Value, CancellationToken.None).ConfigureAwait(false);
            return response;
        }

        if (reply.Status == TransportStatus.Unknown)
        {
            ClientResultResolution resolution = await ResolvePendingAsync(identity.RequestId, cancellationToken).ConfigureAwait(false);
            if (resolution.IsFinal && resolution.Response is not null)
                return resolution.Response;
        }
        return response;
    }

    public ValueTask<IReadOnlyList<PendingCommand>> RestorePendingAsync(CancellationToken cancellationToken = default)
    {
        return _pendingStore.ReadUnresolvedCommandsAsync(cancellationToken);
    }

    public async Task<ClientResultResolution> ResolvePendingAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("RequestId cannot be empty.", nameof(requestId));

        IReadOnlyList<PendingCommand> pending = await _pendingStore.ReadUnresolvedCommandsAsync(cancellationToken).ConfigureAwait(false);
        PendingCommand? command = pending.FirstOrDefault(x => x.Identity.RequestId == requestId);
        if (command is not null)
            return await _resultResolver.ResolveAsync(command, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ResolvedCommand> resolved = await _pendingStore.ReadResolvedAsync(cancellationToken).ConfigureAwait(false);
        ResolvedCommand? saved = resolved.FirstOrDefault(x => x.RequestId == requestId);
        if (saved is not null)
            return new ClientResultResolution(TransportStatus.Final, saved.ResultPayload, null, false, null);

        throw new KeyNotFoundException("RequestId is not present in this client's pending or resolved store.");
    }

    public Task<IReadOnlyList<ClientResultResolution>> ResolveAllPendingAsync(CancellationToken cancellationToken = default)
    {
        return _resultResolver.ResolveAllAsync(cancellationToken);
    }

    private void ValidateIdentity(CommandIdentity identity)
    {
        if (identity.DatasetId != _datasetId)
            throw new InvalidOperationException("DatasetId does not match the authenticated client session.");
        if (!StringComparer.Ordinal.Equals(identity.OwnerId, _ownerId))
            throw new InvalidOperationException("OwnerId does not match the authenticated client session.");
        if (identity.RequestId == Guid.Empty)
            throw new ArgumentException("RequestId cannot be empty.", nameof(identity));
        string expectedHash = CanonicalProtocol.ComputeCommandHashString(
            identity.DatasetId,
            identity.OwnerId,
            identity.Command.Action,
            identity.Command.TripId,
            identity.Command.SeatNo,
            identity.Command.ReservationId);
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedHash, identity.CommandHash))
            throw new InvalidOperationException("CommandHash does not match command identity.");
    }
}
