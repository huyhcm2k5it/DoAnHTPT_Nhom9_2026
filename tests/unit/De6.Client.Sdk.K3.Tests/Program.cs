
using System.Text;
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
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
        failed++;
    }
}

CommandIdentity CreateIdentity(
    Guid datasetId,
    Guid requestId,
    string ownerId,
    int seatNo = 1)
{
    var command = new CommandData(
        BookingAction.Hold,
        1,
        seatNo,
        null);

    string hash = CanonicalProtocol.ComputeCommandHashString(
        datasetId,
        ownerId,
        command.Action,
        command.TripId,
        command.SeatNo,
        command.ReservationId);

    return new CommandIdentity(
        datasetId,
        requestId,
        ownerId,
        hash,
        command);
}

string CreateRoot()
{
    string path = Path.Combine(
        Path.GetTempPath(),
        "De6-K3-Tests",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(path);
    return path;
}

async Task<bool> Throws<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
}

var datasetId = Guid.NewGuid();
const string ownerId = "VanKhoa";
var requestId = Guid.NewGuid();

var identity = CreateIdentity(
    datasetId,
    requestId,
    ownerId);

byte[] canonicalCommand =
    Encoding.UTF8.GetBytes("{\"action\":\"HOLD\",\"tripId\":1,\"seatNo\":1}");

byte[] resultPayload =
    Encoding.UTF8.GetBytes("{\"code\":\"OK_HELD\",\"tripId\":1,\"seatNo\":1}");

await Check(
    "K3-T01 - Save and read pending",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var store = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            await store.SaveBeforeSendAsync(
                identity,
                canonicalCommand,
                CancellationToken.None);

            var pending = await store.ReadUnresolvedAsync(
                CancellationToken.None);

            return pending.Count == 1 &&
                   pending[0] == identity;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T02 - Duplicate same RequestId and command",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var store = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            await store.SaveBeforeSendAsync(
                identity,
                canonicalCommand,
                CancellationToken.None);

            await store.SaveBeforeSendAsync(
                identity,
                canonicalCommand,
                CancellationToken.None);

            var pending = await store.ReadUnresolvedAsync(
                CancellationToken.None);

            return pending.Count == 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T03 - Reject same RequestId with different command",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var store = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            await store.SaveBeforeSendAsync(
                identity,
                canonicalCommand,
                CancellationToken.None);

            var differentIdentity = CreateIdentity(
                datasetId,
                requestId,
                ownerId,
                2);

            byte[] differentCommand =
                Encoding.UTF8.GetBytes("{\"action\":\"HOLD\",\"tripId\":1,\"seatNo\":2}");

            return await Throws<InvalidOperationException>(
                async () =>
                {
                    await store.SaveBeforeSendAsync(
                        differentIdentity,
                        differentCommand,
                        CancellationToken.None);
                });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T04 - Exclusive process lock",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var first = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            bool secondRejected = false;

            try
            {
                await using var second = new FileClientPendingStore(
                    root,
                    datasetId,
                    ownerId);
            }
            catch (InvalidOperationException)
            {
                secondRejected = true;
            }

            return secondRejected;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T05 - Restore pending after restart",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using (var first = new FileClientPendingStore(
                root,
                datasetId,
                ownerId))
            {
                await first.SaveBeforeSendAsync(
                    identity,
                    canonicalCommand,
                    CancellationToken.None);
            }

            await using var second = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            var pending = await second.ReadUnresolvedAsync(
                CancellationToken.None);

            return pending.Count == 1 &&
                   pending[0] == identity;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T06 - Save FINAL and remove pending",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var store = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            await store.SaveBeforeSendAsync(
                identity,
                canonicalCommand,
                CancellationToken.None);

            await store.SaveResolvedAsync(
                requestId,
                resultPayload,
                CancellationToken.None);

            var pending = await store.ReadUnresolvedAsync(
                CancellationToken.None);

            return pending.Count == 0;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T07 - Reject wrong OwnerId",
    async () =>
    {
        string root = CreateRoot();

        try
        {
            await using var store = new FileClientPendingStore(
                root,
                datasetId,
                ownerId);

            var wrongOwnerIdentity = CreateIdentity(
                datasetId,
                Guid.NewGuid(),
                "AnotherUser");

            return await Throws<InvalidOperationException>(
                async () =>
                {
                    await store.SaveBeforeSendAsync(
                        wrongOwnerIdentity,
                        canonicalCommand,
                        CancellationToken.None);
                });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T08 - Restore canonical command bytes after restart",
    async () =>
    {
        string root = CreateRoot();
        try
        {
            await using (var first = new FileClientPendingStore(root, datasetId, ownerId))
            {
                await first.SaveBeforeSendAsync(identity, canonicalCommand, CancellationToken.None);
            }
            await using var second = new FileClientPendingStore(root, datasetId, ownerId);
            IReadOnlyList<PendingCommand> pending = await second.ReadUnresolvedCommandsAsync(CancellationToken.None);
            return pending.Count == 1 && pending[0].Identity == identity &&
                pending[0].CanonicalCommand.Span.SequenceEqual(canonicalCommand) &&
                pending[0].SavedUtc != default;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

await Check(
    "K3-T09 - Restore resolved result bytes after restart",
    async () =>
    {
        string root = CreateRoot();
        try
        {
            await using (var first = new FileClientPendingStore(root, datasetId, ownerId))
            {
                await first.SaveBeforeSendAsync(identity, canonicalCommand, CancellationToken.None);
                await first.SaveResolvedAsync(requestId, resultPayload, CancellationToken.None);
            }
            await using var second = new FileClientPendingStore(root, datasetId, ownerId);
            IReadOnlyList<ResolvedCommand> resolved = await second.ReadResolvedAsync(CancellationToken.None);
            return resolved.Count == 1 && resolved[0].RequestId == requestId &&
                resolved[0].ResultPayload.Span.SequenceEqual(resultPayload) && resolved[0].ResolvedUtc != default;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    });

Console.WriteLine();
Console.WriteLine("================================");
Console.WriteLine("       K3 PENDING STORE TESTS");
Console.WriteLine("================================");
Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");
Console.WriteLine("================================");

if (failed == 0)
{
    Console.WriteLine("ALL TESTS PASSED!");
}
else
{
    Console.WriteLine("SOME TESTS FAILED!");
}

return failed == 0 ? 0 : 1;
