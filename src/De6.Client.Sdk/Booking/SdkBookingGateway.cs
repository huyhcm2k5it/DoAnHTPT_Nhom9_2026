using System.Text;
using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

/// <summary>Adapter from the UI controller to the K3-K5 SDK (pending store, route manager, result resolver).</summary>
public sealed class SdkBookingGateway : IBookingGateway
{
    private static readonly byte[] GetSeatsBytes = Encoding.UTF8.GetBytes("{\"type\":\"GET_SEATS\"}");
    private static readonly byte[] GetReservationsBytes = Encoding.UTF8.GetBytes("{\"type\":\"GET_MY_RESERVATIONS\"}");

    private readonly IClientPendingStore _store;
    private readonly ClientRouteManager _routes;
    private readonly ClientCommandExecutor _executor;

    public SdkBookingGateway(ClientOptions options, string ownerId, IClientPendingStore store, ClientRouteManager routes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(routes);
        _store = store;
        _routes = routes;
        _executor = new ClientCommandExecutor(store, routes, options.DatasetId, ownerId);
    }

    public ClientNodeStatus? ActiveStatus => _routes.ActiveStatus;

    public async Task<StableSnapshot> ReadSeatsAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> result = await ReadAsync(GetSeatsBytes, cancellationToken).ConfigureAwait(false);
        return ReadModelCodec.ParseSeats(result);
    }

    public async Task<ReservationsSnapshot> ReadMyReservationsAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> result = await ReadAsync(GetReservationsBytes, cancellationToken).ConfigureAwait(false);
        return ReadModelCodec.ParseReservations(result);
    }

    public async Task<CommandOutcome> ExecuteAsync(CommandIdentity identity, ReadOnlyMemory<byte> canonicalCommand, CancellationToken cancellationToken)
    {
        try
        {
            ClientEnvelope response = await _executor.ExecuteAsync(identity, canonicalCommand, _ => null, cancellationToken).ConfigureAwait(false);
            return Map(identity.RequestId, ClientReplyPayload.Parse(response.Payload));
        }
        catch (Exception exception) when (IsTransport(exception))
        {
            return new CommandOutcome(identity.RequestId, CommandOutcomeKind.Unresolved, null, TransportStatus.Unknown, Describe(exception));
        }
    }

    public async Task<IReadOnlyList<PendingCommand>> ReadPendingAsync(CancellationToken cancellationToken) =>
        await _executor.RestorePendingAsync(cancellationToken).ConfigureAwait(false);

    public async Task<CommandOutcome> ResolveAsync(Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            ClientResultResolution resolution = await _executor.ResolvePendingAsync(requestId, cancellationToken).ConfigureAwait(false);
            if (resolution.IsFinal)
                return new CommandOutcome(requestId, CommandOutcomeKind.Final,
                    CanonicalProtocol.DeserializeBusinessResult(resolution.ResultPayload!.Value.Span), TransportStatus.Final, null);
            string detail = resolution.TransportError is { } error ? Describe(error) : $"Cluster trả {resolution.Status}";
            CommandOutcomeKind kind = resolution.Status == TransportStatus.Rejected ? CommandOutcomeKind.Rejected : CommandOutcomeKind.Unresolved;
            return new CommandOutcome(requestId, kind, null, resolution.Status, detail);
        }
        catch (Exception exception) when (IsTransport(exception))
        {
            return new CommandOutcome(requestId, CommandOutcomeKind.Unresolved, null, TransportStatus.Unknown, Describe(exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _routes.DisposeAsync().ConfigureAwait(false);
        await _store.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ReadOnlyMemory<byte>> ReadAsync(byte[] payloadBytes, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(payloadBytes);
        ClientEnvelope response = await _routes.SendAsync(new ClientRequest(null, document.RootElement.Clone()), cancellationToken).ConfigureAwait(false);
        ClientReplyPayload reply = ClientReplyPayload.Parse(response.Payload);
        if (reply.Status != TransportStatus.Final || reply.ResultPayload is null)
            throw new ClientReadNotReadyException(reply.Status);
        return reply.ResultPayload.Value;
    }

    private static CommandOutcome Map(Guid requestId, ClientReplyPayload reply) => reply.Status switch
    {
        TransportStatus.Final => new CommandOutcome(requestId, CommandOutcomeKind.Final,
            CanonicalProtocol.DeserializeBusinessResult(reply.ResultPayload!.Value.Span), TransportStatus.Final, null),
        TransportStatus.Rejected => new CommandOutcome(requestId, CommandOutcomeKind.Rejected, null, reply.Status, "Cluster từ chối khung lệnh."),
        _ => new CommandOutcome(requestId, CommandOutcomeKind.Unresolved, null, reply.Status, $"Cluster trả {reply.Status}; lệnh được giữ để tra lại cùng RequestId.")
    };

    private static bool IsTransport(Exception e) =>
        e is ClientTransportUncertainException or StaleClientResponseException or ClientRouteException or IOException or TimeoutException;

    private static string Describe(Exception e) => e switch
    {
        ClientRouteException => "Không có node PRIMARY_READY hợp lệ.",
        StaleClientResponseException => "Response đến từ kết nối cũ đã bị bỏ.",
        _ => "Mất kết nối khi chờ phản hồi; kết quả chưa xác định."
    };
}
