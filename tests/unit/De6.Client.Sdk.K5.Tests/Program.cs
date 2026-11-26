using System.Text;
using System.Text.Json;
using De6.Client.Sdk;
using De6.Contracts;

int passed = 0;
int failed = 0;

async Task Check(string name, Func<Task<bool>> test)
{
    try
    {
        if (await test()) { Console.WriteLine($"[PASS] {name}"); passed++; }
        else { Console.WriteLine($"[FAIL] {name}"); failed++; }
    }
    catch (Exception exception) { Console.WriteLine($"[FAIL] {name}: {exception.GetType().Name}: {exception.Message}"); failed++; }
}

const string owner = "A";
Guid dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
Guid requestId = Guid.Parse("224a4f06-4ac1-4c12-8b27-202600000009");
byte[] commandBytes = Encoding.UTF8.GetBytes("{\"action\":\"HOLD\",\"tripId\":1,\"seatNo\":1}");
byte[] resultBytes = Encoding.UTF8.GetBytes("{\"code\":\"OK_HELD\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null,\"ticketId\":null}");
CommandIdentity identity = CreateIdentity(dataset, requestId, owner);

await Check("K5-T01 - GET_RESULT FINAL lưu result và xóa pending", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        await store.SaveBeforeSendAsync(identity, commandBytes, CancellationToken.None);
        var factory = new FakeFactory(dataset, [Reply("FINAL", JsonDocument.Parse(Encoding.UTF8.GetString(resultBytes)).RootElement)]);
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var resolver = new ClientResultResolver(store, routes, dataset, owner);
        ClientResultResolution resolved = await resolver.ResolveAsync(new PendingCommand(identity, commandBytes, DateTime.UtcNow));
        IReadOnlyList<PendingCommand> pending = await store.ReadUnresolvedCommandsAsync(CancellationToken.None);
        IReadOnlyList<ResolvedCommand> saved = await store.ReadResolvedAsync(CancellationToken.None);
        return resolved.IsFinal && pending.Count == 0 && saved.Count == 1 &&
            saved[0].RequestId == requestId && saved[0].ResultPayload.Span.SequenceEqual(resultBytes) &&
            factory.Primary.Sent.Count == 1 && factory.Primary.Sent[0].RequestId == requestId &&
            factory.Primary.Sent[0].Payload.GetProperty("type").GetString() == "GET_RESULT";
    }
    finally { Directory.Delete(root, true); }
});

await Check("K5-T02 - NOT_FOUND retry đúng RequestId và canonical command", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        await store.SaveBeforeSendAsync(identity, commandBytes, CancellationToken.None);
        var factory = new FakeFactory(dataset, [Reply("NOT_FOUND"), Reply("FINAL", JsonDocument.Parse(Encoding.UTF8.GetString(resultBytes)).RootElement)]);
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var resolver = new ClientResultResolver(store, routes, dataset, owner);
        ClientResultResolution resolved = await resolver.ResolveAsync(new PendingCommand(identity, commandBytes, DateTime.UtcNow));
        ClientEnvelope retry = factory.Primary.Sent[1];
        return resolved.IsFinal && resolved.Retried && factory.Primary.Sent.Count == 2 &&
            factory.Primary.Sent[0].RequestId == requestId && factory.Primary.Sent[1].RequestId == requestId &&
            retry.Payload.GetRawText() == Encoding.UTF8.GetString(commandBytes) &&
            (await store.ReadUnresolvedCommandsAsync(CancellationToken.None)).Count == 0;
    }
    finally { Directory.Delete(root, true); }
});

await Check("K5-T03 - UNKNOWN giữ pending và không retry mutation", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        await store.SaveBeforeSendAsync(identity, commandBytes, CancellationToken.None);
        var factory = new FakeFactory(dataset, [Reply("UNKNOWN")]);
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var resolver = new ClientResultResolver(store, routes, dataset, owner);
        ClientResultResolution resolved = await resolver.ResolveAsync(new PendingCommand(identity, commandBytes, DateTime.UtcNow));
        return resolved.Status == TransportStatus.Unknown && !resolved.IsFinal && factory.Primary.Sent.Count == 1 &&
            (await store.ReadUnresolvedCommandsAsync(CancellationToken.None)).Count == 1;
    }
    finally { Directory.Delete(root, true); }
});

await Check("K5-T04 - Disconnect khi GET_RESULT giữ pending", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        await store.SaveBeforeSendAsync(identity, commandBytes, CancellationToken.None);
        var factory = new FakeFactory(dataset, [] ) { PrimaryFailNext = true };
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var resolver = new ClientResultResolver(store, routes, dataset, owner);
        ClientResultResolution resolved = await resolver.ResolveAsync(new PendingCommand(identity, commandBytes, DateTime.UtcNow));
        return !resolved.IsFinal && resolved.TransportError is not null &&
            (await store.ReadUnresolvedCommandsAsync(CancellationToken.None)).Count == 1;
    }
    finally { Directory.Delete(root, true); }
});

await Check("K5-T06 - Execute UNKNOWN tự GET_RESULT rồi lưu FINAL", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        var finalResult = JsonDocument.Parse(Encoding.UTF8.GetString(resultBytes)).RootElement;
        var factory = new FakeFactory(dataset, [Reply("UNKNOWN"), Reply("FINAL", finalResult)]);
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var executor = new ClientCommandExecutor(store, routes, dataset, owner);
        ClientEnvelope finalResponse = await executor.ExecuteAsync(identity, commandBytes,
            response => Encoding.UTF8.GetBytes(response.Payload.GetProperty("result").GetRawText()));
        IReadOnlyList<ResolvedCommand> saved = await store.ReadResolvedAsync(CancellationToken.None);
        return finalResponse.Payload.GetProperty("status").GetString() == "FINAL" &&
            factory.Primary.Sent.Count == 2 && factory.Primary.Sent[0].RequestId == requestId &&
            factory.Primary.Sent[1].RequestId == requestId &&
            factory.Primary.Sent[0].Payload.GetRawText() == Encoding.UTF8.GetString(commandBytes) &&
            factory.Primary.Sent[1].Payload.GetProperty("type").GetString() == "GET_RESULT" &&
            saved.Count == 1 && saved[0].ResultPayload.Span.SequenceEqual(resultBytes);
    }
    finally { Directory.Delete(root, true); }
});

await Check("K5-T05 - Từ chối pending khác OwnerId", async () =>
{
    string root = NewRoot();
    try
    {
        await using var store = new FileClientPendingStore(root, dataset, owner);
        var factory = new FakeFactory(dataset, []);
        await using var routes = new ClientRouteManager(Options(dataset), factory);
        var resolver = new ClientResultResolver(store, routes, dataset, owner);
        CommandIdentity other = CreateIdentity(dataset, requestId, "B");
        try { await resolver.ResolveAsync(new PendingCommand(other, commandBytes, DateTime.UtcNow)); return false; }
        catch (InvalidOperationException) { return factory.Primary.Sent.Count == 0; }
    }
    finally { Directory.Delete(root, true); }
});

Console.WriteLine($"K5 result: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static string NewRoot()
{
    string path = Path.Combine(Path.GetTempPath(), "De6-K5-Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static CommandIdentity CreateIdentity(Guid datasetId, Guid id, string ownerId)
{
    var command = new CommandData(BookingAction.Hold, 1, 1, null);
    string hash = CanonicalProtocol.ComputeCommandHashString(datasetId, ownerId, command.Action, command.TripId, command.SeatNo, command.ReservationId);
    return new CommandIdentity(datasetId, id, ownerId, hash, command);
}

static ClientOptions Options(Guid datasetId) => new(1, "A", datasetId,
    [new(1, "127.0.0.1", 9001, 9101), new(2, "127.0.0.1", 9002, 9102), new(3, "127.0.0.1", 9003, 9103)], "data/clients/c1");

static ClientEnvelope Reply(string status, JsonElement? result = null) => new(1,
    Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"), Guid.NewGuid(), Guid.Empty, 1,
    BuildPayload(status, result));

static JsonElement BuildPayload(string status, JsonElement? result)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject(); writer.WriteString("status", status);
        if (result is { } value) { writer.WritePropertyName("result"); value.WriteTo(writer); }
        writer.WriteEndObject();
    }
    using JsonDocument document = JsonDocument.Parse(stream.ToArray());
    return document.RootElement.Clone();
}

sealed class FakeFactory(Guid datasetId, IReadOnlyList<ClientEnvelope> replies) : IClientNodeConnectionFactory
{
    public FakeConnection Primary { get; } = new(datasetId, replies);
    public bool PrimaryFailNext { get => Primary.FailNext; set => Primary.FailNext = value; }
    public Task<IClientNodeConnection> ConnectAsync(NodeEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.NodeId == 1) return Task.FromResult<IClientNodeConnection>(Primary);
        return Task.FromResult<IClientNodeConnection>(new FakeConnection(datasetId, replies, endpoint.NodeId, ClientNodeRole.BackupReady));
    }
}

sealed class FakeConnection(Guid datasetId, IReadOnlyList<ClientEnvelope> replies, int nodeId = 1, ClientNodeRole role = ClientNodeRole.PrimaryReady) : IClientNodeConnection
{
    private int _replyIndex;
    public int NodeId { get; } = nodeId;
    public bool FailNext { get; set; }
    public List<ClientEnvelope> Sent { get; } = [];
    public Task<ClientNodeStatus> GetClusterStatusAsync(Guid requestedDataset, CancellationToken cancellationToken) =>
        Task.FromResult(new ClientNodeStatus(NodeId, datasetId, role, 1, role == ClientNodeRole.PrimaryReady ? NodeId : 1, true, 0, 0, 0));
    public Task<ClientEnvelope> SendAsync(ClientEnvelope request, CancellationToken cancellationToken)
    {
        Sent.Add(request);
        if (FailNext) { FailNext = false; throw new IOException("Simulated disconnect"); }
        if (_replyIndex >= replies.Count) throw new IOException("No fake reply configured.");
        ClientEnvelope template = replies[_replyIndex++];
        return Task.FromResult(template with { CorrelationId = request.CorrelationId, SenderId = NodeId, DatasetId = datasetId });
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
