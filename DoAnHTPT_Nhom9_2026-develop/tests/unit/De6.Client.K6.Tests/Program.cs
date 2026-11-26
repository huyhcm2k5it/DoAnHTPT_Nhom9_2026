using De6.Client.Sdk;
using De6.Client.Simulation;
using De6.Contracts;

// K6 controller/UI-logic tests. They run the REAL client SDK (pending store, route manager, resolver, controller)
// against a FAKE in-process cluster (De6.Client.Simulation). They do not prove server durability, FINAL replication,
// or any T01-T36 PASS; see docs/testing/K6_DEMO_RUNBOOK.md.
int passed = 0, failed = 0;
async Task Check(string name, Func<Task<bool>> test)
{
    try { if (await test()) { Console.WriteLine($"[PASS] {name}"); passed++; } else { Console.WriteLine($"[FAIL] {name}"); failed++; } }
    catch (Exception e) { Console.WriteLine($"[FAIL] {name}: {e.GetType().Name}: {e.Message}"); failed++; }
}

Guid dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
NodeEndpoint[] nodes = [new(1, "127.0.0.1", 9001, 9101), new(2, "127.0.0.1", 9002, 9102), new(3, "127.0.0.1", 9003, 9103)];

string NewRoot() { string p = Path.Combine(Path.GetTempPath(), "de6-k6-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }

BookingController Client(string root, int clientId, SimulatedFaults? faults = null, string? subdir = null)
{
    string owner = ((char)('A' + clientId - 1)).ToString();
    var options = new ClientOptions(clientId, owner, dataset, nodes, $"data/clients/c{clientId}");
    var cluster = new SimulatedCluster(Path.Combine(root, "cluster"), dataset);
    var store = new FileClientPendingStore(Path.Combine(root, subdir ?? $"c{clientId}"), dataset, owner);
    var routes = new ClientRouteManager(options, new SimulatedNodeConnectionFactory(cluster, owner, faults ?? new SimulatedFaults(), latencyMs: 1));
    return new BookingController(new SdkBookingGateway(options, owner, store, routes), owner, dataset, clientId);
}

SeatCell Cell(BookingController c, int seat) => c.BuildCells().First(x => x.SeatNo == seat);

await Check("K6-01 board loads 10 seats AVAILABLE, none pending", async () =>
{
    string root = NewRoot();
    await using var c = Client(root, 1);
    await c.StartAsync();
    return c.BuildCells().Count == 10 && c.BuildCells().All(x => x.Status == SeatStatus.Available && x.Pending is null) && !c.BoardIsStale;
});

await Check("K6-02 three counters HOLD same seat: exactly one OK_HELD, others SEAT_BUSY (T02 shape, fake cluster)", async () =>
{
    string root = NewRoot();
    await using var a = Client(root, 1); await using var b = Client(root, 2); await using var c = Client(root, 3);
    await Task.WhenAll(a.StartAsync(), b.StartAsync(), c.StartAsync());
    var all = await Task.WhenAll(a.SubmitAsync(BookingAction.Hold, 5), b.SubmitAsync(BookingAction.Hold, 5), c.SubmitAsync(BookingAction.Hold, 5));
    var codes = all.Where(o => o is not null).Select(o => o!.Result!.Code).ToList();
    return codes.Count(x => x == ResultCodes.OkHeld) == 1 && codes.Count(x => x == ResultCodes.SeatBusy) == 2;
});

await Check("K6-03 HOLD then CONFIRM: seat SOLD, ticket issued, FINAL shown as success", async () =>
{
    string root = NewRoot();
    await using var a = Client(root, 1);
    await a.StartAsync();
    await a.SubmitAsync(BookingAction.Hold, 2);
    bool held = Cell(a, 2) is { Status: SeatStatus.Held, HeldByMe: true } && a.GetActions(2) is { CanConfirm: true, CanCancel: true, CanHold: false };
    CommandOutcome? sold = await a.SubmitAsync(BookingAction.Confirm, 2);
    return held && sold?.Result is { Code: ResultCodes.OkSold, TicketId: not null } && Cell(a, 2).Status == SeatStatus.Sold && a.Last is { Success: true };
});

await Check("K6-04 HOLD then CANCEL then HOLD again", async () =>
{
    string root = NewRoot();
    await using var a = Client(root, 1);
    await a.StartAsync();
    await a.SubmitAsync(BookingAction.Hold, 3);
    var cancel = await a.SubmitAsync(BookingAction.Cancel, 3);
    var again = await a.SubmitAsync(BookingAction.Hold, 3);
    return cancel?.Result?.Code == ResultCodes.OkCancelled && again?.Result?.Code == ResultCodes.OkHeld && a.MyReservations.Count == 2;
});

await Check("K6-05 other counter's HELD seat is not mine and offers no CONFIRM/CANCEL", async () =>
{
    string root = NewRoot();
    await using var a = Client(root, 1); await using var b = Client(root, 2);
    await a.StartAsync(); await b.StartAsync();
    await a.SubmitAsync(BookingAction.Hold, 4);
    await b.RefreshAsync();
    return Cell(b, 4) is { Status: SeatStatus.Held, HeldByMe: false } && b.GetActions(4) == SeatActions.None;
});

await Check("K6-06 lost response + network cut: UNKNOWN pending kept, no success shown, same RequestId resolves to FINAL (T17 shape)", async () =>
{
    string root = NewRoot();
    var faults = new SimulatedFaults();
    await using var a = Client(root, 1, faults);
    await a.StartAsync();
    faults.CutAfterNextApply = true;
    CommandOutcome? outcome = await a.SubmitAsync(BookingAction.Hold, 6);
    bool unknown = outcome is { Kind: CommandOutcomeKind.Unresolved } && a.Pending.Count == 1 && a.Last is null &&
        a.GetActions(6) == SeatActions.None && Cell(a, 6).Pending is not null;
    Guid requestId = a.Pending[0].RequestId;
    faults.NetworkDown = false;
    await a.ResolvePendingAsync();
    return unknown && a.Pending.Count == 0 && a.Last is { RequestId: var id, Code: ResultCodes.OkHeld, Success: true } && id == requestId &&
        a.Metrics.ResolvedLater == 1 && Cell(a, 6) is { Status: SeatStatus.Held, HeldByMe: true } && !a.BoardIsStale;
});

await Check("K6-07 client restart with UNKNOWN pending: restored, resolved, no duplicate event", async () =>
{
    string root = NewRoot();
    var faults = new SimulatedFaults();
    Guid requestId;
    await using (var a = Client(root, 1, faults))
    {
        await a.StartAsync();
        faults.CutAfterNextApply = true;
        await a.SubmitAsync(BookingAction.Hold, 7);
        requestId = a.Pending[0].RequestId;
    }
    long indexBefore = new SimulatedCluster(Path.Combine(root, "cluster"), dataset).AppliedIndex;
    await using var restarted = Client(root, 1, new SimulatedFaults());
    await restarted.StartAsync();
    long indexAfter = new SimulatedCluster(Path.Combine(root, "cluster"), dataset).AppliedIndex;
    return restarted.Pending.Count == 0 && restarted.Last?.RequestId == requestId && restarted.Last.Code == ResultCodes.OkHeld &&
        indexBefore == indexAfter && Cell(restarted, 7).HeldByMe;
});

await Check("K6-08 restart while still offline: pending shown as UNKNOWN, board stale, nothing invented", async () =>
{
    string root = NewRoot();
    var faults = new SimulatedFaults();
    await using (var a = Client(root, 1, faults))
    {
        await a.StartAsync();
        faults.CutAfterNextApply = true;
        await a.SubmitAsync(BookingAction.Hold, 8);
    }
    var down = new SimulatedFaults { NetworkDown = true };
    await using var restarted = Client(root, 1, down);
    await restarted.StartAsync();
    return restarted.Pending.Count == 1 && restarted.Pending[0].Phase == PendingPhase.Unknown && restarted.Board is null &&
        restarted.BuildCells().All(x => x.Status is null) && restarted.Last is null;
});

await Check("K6-09 dropped response only: SDK resolves via GET_RESULT, exactly one event", async () =>
{
    string root = NewRoot();
    var faults = new SimulatedFaults();
    await using var a = Client(root, 1, faults);
    await a.StartAsync();
    long before = new SimulatedCluster(Path.Combine(root, "cluster"), dataset).AppliedIndex;
    faults.DropNextResponse = true;
    CommandOutcome? o = await a.SubmitAsync(BookingAction.Hold, 9);
    long after = new SimulatedCluster(Path.Combine(root, "cluster"), dataset).AppliedIndex;
    return o?.Result?.Code == ResultCodes.OkHeld && after == before + 1 && a.Pending.Count == 0;
});

await Check("K6-10 two counters keep separate pending stores; same store cannot be opened twice (T34 shape)", async () =>
{
    string root = NewRoot();
    var o = new ClientOptions(1, "A", dataset, nodes, "data/clients/c1");
    await using var s1 = new FileClientPendingStore(Path.Combine(root, "c1"), dataset, "A");
    await using var s2 = new FileClientPendingStore(Path.Combine(root, "c2"), dataset, "B");
    try { await using var dup = new FileClientPendingStore(Path.Combine(root, "c1"), dataset, "A"); return false; }
    catch (InvalidOperationException) { return true; }
});

await Check("K6-11 local precondition blocks HOLD on a HELD seat without sending", async () =>
{
    string root = NewRoot();
    await using var a = Client(root, 1); await using var b = Client(root, 2);
    await a.StartAsync(); await b.StartAsync();
    await a.SubmitAsync(BookingAction.Hold, 1);
    await b.RefreshAsync();
    int sentBefore = b.Metrics.Sent;
    CommandOutcome? o = await b.SubmitAsync(BookingAction.Hold, 1);
    return o is null && b.Metrics.Sent == sentBefore;
});

await Check("K6-12 read-model codec round trip", async () =>
{
    string root = NewRoot();
    var cluster = new SimulatedCluster(Path.Combine(root, "cluster"), dataset);
    var snapshot = cluster.ReadSeats();
    var parsed = ReadModelCodec.ParseSeats(ReadModelCodec.WriteSeats(snapshot));
    await Task.CompletedTask;
    return parsed.Seats.Count == 10 && parsed.DatasetId == dataset && parsed.Seats.All(s => s.Status == SeatStatus.Available);
});

await Check("K6-13 canonical command bytes match the V12 fixture shape", async () =>
{
    byte[] hold = ReadModelCodec.WriteCanonicalCommand(new CommandData(BookingAction.Hold, 1, 1, null));
    await Task.CompletedTask;
    return System.Text.Encoding.UTF8.GetString(hold) == "{\"action\":\"HOLD\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null}";
});

Console.WriteLine($"{passed}/{passed + failed} assertions passed (fake cluster; not T-matrix evidence).");
return failed == 0 ? 0 : 1;
