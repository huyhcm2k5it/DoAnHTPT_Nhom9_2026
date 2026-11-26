using System.Text.Json;
using De6.Client.Sdk;
using De6.Contracts;

int passed = 0;
int failed = 0;

async Task Check(string name, Func<Task<bool>> test)
{
    try
    {
        if (await test())
        {
            Console.WriteLine($"[PASS] {name}");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] {name}");
            failed++;
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"[FAIL] {name}: {exception.GetType().Name}: {exception.Message}");
        failed++;
    }
}

ClientOptions CreateOptions(Guid datasetId) => new(
    1,
    "A",
    datasetId,
    [
        new NodeEndpoint(1, "127.0.0.1", 9001, 9101),
        new NodeEndpoint(2, "127.0.0.1", 9002, 9102),
        new NodeEndpoint(3, "127.0.0.1", 9003, 9103)
    ],
    "data/clients/c1");

JsonElement Payload(string value) => JsonDocument.Parse(value).RootElement.Clone();

var dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");

await Check("K4-T01 - Chỉ chọn node PRIMARY_READY", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.BackupReady,
        [2] = ClientNodeRole.PrimaryReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    ClientNodeStatus status = await router.RefreshPrimaryAsync();
    return status.NodeId == 2 && factory.Created.Count == 3 && factory.Created[0].Disposed && factory.Created[2].Disposed;
});

await Check("K4-T02 - Không route khi có hai PRIMARY_READY", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.PrimaryReady,
        [3] = ClientNodeRole.BackupReady
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    try
    {
        await router.RefreshPrimaryAsync();
        return false;
    }
    catch (ClientRouteException)
    {
        return router.ActiveStatus is null;
    }
});

await Check("K4-T03 - Response đúng CorrelationId được nhận", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    ClientEnvelope response = await router.SendAsync(new ClientRequest(Guid.NewGuid(), Payload("{\"type\":\"PING\"}")));
    return response.CorrelationId != Guid.Empty && response.DatasetId == dataset;
});

await Check("K4-T04 - Mất kết nối trả trạng thái outcome uncertain và reconnect", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    Guid requestId = Guid.NewGuid();
    factory.Created[0].FailNextSend = true;
    try
    {
        await router.SendAsync(new ClientRequest(requestId, Payload("{\"requestId\":\"" + requestId.ToString("D") + "\"}")));
        return false;
    }
    catch (ClientTransportUncertainException exception)
    {
        return exception.RequestId == requestId && router.ActiveStatus?.Role == ClientNodeRole.PrimaryReady;
    }
});

await Check("K4-T05 - Bỏ response đến muộn từ connection cũ", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    FakeConnection oldConnection = factory.Created[0];
    TaskCompletionSource<bool> blockedSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool> sendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    oldConnection.BlockedSend = blockedSend;
    oldConnection.SendStarted = sendStarted;
    Guid staleRequestId = Guid.NewGuid();
    Task<ClientEnvelope> send = router.SendAsync(new ClientRequest(staleRequestId, Payload("{\"type\":\"PING\"}")));
    await sendStarted.Task;
    await router.RefreshPrimaryAsync();
    blockedSend.SetResult(true);
    try
    {
        await send;
        return false;
    }
    catch (StaleClientResponseException exception)
    {
        return exception.RequestId == staleRequestId && exception.CorrelationId != Guid.Empty &&
            oldConnection.Disposed && router.ActiveStatus?.NodeId == 1;
    }
});

await Check("K4-T06 - Từ chối response có CorrelationId không khớp", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    factory.Created[0].WrongCorrelation = true;
    try
    {
        await router.SendAsync(new ClientRequest(null, Payload("{\"type\":\"PING\"}")));
        return false;
    }
    catch (ClientTransportUncertainException)
    {
        return true;
    }
});

await Check("K4-T07 - Không route tới PRIMARY_READY khi SQL không khỏe", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    }) { SqlHealthy = false };
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    try
    {
        await router.RefreshPrimaryAsync();
        return false;
    }
    catch (ClientRouteException)
    {
        return router.ActiveStatus is null;
    }
});

await Check("K4-T08 - Đổi epoch buộc thay connection generation trước khi gửi", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    FakeConnection oldConnection = factory.Created[0];
    factory.Epoch = 2;
    oldConnection.Epoch = 2;
    ClientEnvelope response = await router.SendAsync(new ClientRequest(Guid.NewGuid(), Payload("{\"type\":\"PING\"}")));
    return oldConnection.Disposed && factory.Created.Count == 6 &&
        router.ActiveStatus?.Epoch == 2 && response.CorrelationId != Guid.Empty;
});

await Check("K4-T09 - Hủy trong lúc chờ response vẫn báo outcome uncertain", async () =>
{
    var factory = new FakeFactory(dataset, new Dictionary<int, ClientNodeRole>
    {
        [1] = ClientNodeRole.PrimaryReady,
        [2] = ClientNodeRole.BackupReady,
        [3] = ClientNodeRole.Recovering
    });
    await using var router = new ClientRouteManager(CreateOptions(dataset), factory);
    await router.RefreshPrimaryAsync();
    using var cancellation = new CancellationTokenSource();
    factory.Created[0].CancelSource = cancellation;
    Guid requestId = Guid.NewGuid();
    try
    {
        await router.SendAsync(new ClientRequest(requestId, Payload("{\"type\":\"PING\"}")), cancellation.Token);
        return false;
    }
    catch (ClientTransportUncertainException exception)
    {
        return exception.RequestId == requestId && router.ActiveStatus?.Role == ClientNodeRole.PrimaryReady;
    }
});

Console.WriteLine($"K4 result: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeFactory : IClientNodeConnectionFactory
{
    private readonly Guid _datasetId;
    private readonly IReadOnlyDictionary<int, ClientNodeRole> _roles;
    public List<FakeConnection> Created { get; } = [];
    public bool SqlHealthy { get; set; } = true;
    public long Epoch { get; set; } = 1;

    public FakeFactory(Guid datasetId, IReadOnlyDictionary<int, ClientNodeRole> roles)
    {
        _datasetId = datasetId;
        _roles = roles;
    }

    public Task<IClientNodeConnection> ConnectAsync(NodeEndpoint endpoint, CancellationToken cancellationToken)
    {
        var connection = new FakeConnection(endpoint.NodeId, _datasetId, _roles[endpoint.NodeId], SqlHealthy, Epoch);
        Created.Add(connection);
        return Task.FromResult<IClientNodeConnection>(connection);
    }
}

sealed class FakeConnection : IClientNodeConnection
{
    private readonly Guid _datasetId;
    private readonly ClientNodeRole _role;
    private readonly bool _sqlHealthy;
    public long Epoch { get; set; }
    public int NodeId { get; }
    public bool Disposed { get; private set; }
    public bool FailNextSend { get; set; }
    public bool WrongCorrelation { get; set; }
    public TaskCompletionSource<bool>? BlockedSend { get; set; }
    public TaskCompletionSource<bool>? SendStarted { get; set; }
    public CancellationTokenSource? CancelSource { get; set; }

    public FakeConnection(int nodeId, Guid datasetId, ClientNodeRole role, bool sqlHealthy, long epoch)
    {
        NodeId = nodeId;
        _datasetId = datasetId;
        _role = role;
        _sqlHealthy = sqlHealthy;
        Epoch = epoch;
    }

    public Task<ClientNodeStatus> GetClusterStatusAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ClientNodeStatus(NodeId, _datasetId, _role, Epoch,
            _role == ClientNodeRole.PrimaryReady ? NodeId : 1, _sqlHealthy, 10, 10, 0));
    }

    public async Task<ClientEnvelope> SendAsync(ClientEnvelope request, CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancelSource = CancelSource;
        if (cancelSource is not null)
        {
            cancelSource.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
        TaskCompletionSource<bool>? blockedSend = BlockedSend;
        if (blockedSend is not null)
        {
            SendStarted?.TrySetResult(true);
            await blockedSend.Task.WaitAsync(cancellationToken);
        }
        if (FailNextSend)
        {
            FailNextSend = false;
            throw new IOException("Simulated disconnect");
        }
        return new ClientEnvelope(
            1,
            _datasetId,
            Guid.NewGuid(),
            WrongCorrelation ? Guid.NewGuid() : request.CorrelationId,
            NodeId,
            JsonDocument.Parse("{\"type\":\"ACK\"}").RootElement.Clone());
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
