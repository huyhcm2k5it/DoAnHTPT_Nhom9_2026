using System.Text.Json;
using De6.Booking.Domain;
using De6.Contracts;

namespace De6.Domain.UnitTests;

internal static partial class Program
{
    private static async Task RunWeekOneHandoffTestsAsync()
    {
        var utc = new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc);
        var reservationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var requestId = Guid.Parse("22222222-3333-4444-5555-666666666666");
        var eventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var command = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, requestId, "A", 1);
        var result = new BusinessResult(ResultCodes.OkHeld, 1, 1, reservationId, null);
        var decision = BookingDecisionEngine.Decide(command.Identity.Command, "A",
            new SeatView(1, 1, SeatStatus.Available, null, null, 0), null, null, 1, 1, 1, utc, reservationId);
        var payload = new EventPayloadData(Fixtures.SampleDatasetId, 1, 1, 1, eventId,
            Fixtures.ExpectedSeedHash, requestId, "A", Fixtures.ExpectedHoldHash, command.Identity.Command,
            decision, 0, result, utc);
        byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

        RunTest("Q1 handoff: fixed HOLD event/result bytes and hashes", () =>
        {
            AssertTrue(ReadFixture("hold.event.json").SequenceEqual(CanonicalProtocol.SerializeEventPayload(payload)));
            AssertTrue(ReadFixture("hold.result.json").SequenceEqual(CanonicalProtocol.SerializeBusinessResult(result)));
            using var expected = JsonDocument.Parse(ReadFixture("hold.input.json"));
            AssertEqual(expected.RootElement.GetProperty("commandHash").GetString(), command.Identity.CommandHash);
            AssertEqual(expected.RootElement.GetProperty("eventHash").GetString(),
                CanonicalProtocol.ComputeEventHashHex(Convert.FromHexString(Fixtures.ExpectedSeedHash), ReadFixture("hold.event.json")));
        });

        using var receipts = JsonDocument.Parse(ReadFixture("hold.receipts.json"));
        foreach (var entry in receipts.RootElement.EnumerateArray())
        {
            var row = entry.Clone();
            RunTest("Q1 fixed receipt fields: " + row.GetProperty("name").GetString(), () =>
            {
                var receipt = row.GetProperty("receipt");
                AssertEqual(row.GetProperty("nodeId").GetInt32(), receipt.GetProperty("nodeId").GetInt32());
                AssertEqual(1L + row.GetProperty("authorityEpochDelta").GetInt64(), receipt.GetProperty("authorityEpoch").GetInt64());
                AssertEqual(1L + row.GetProperty("eventEpochDelta").GetInt64(), receipt.GetProperty("eventEpoch").GetInt64());
                AssertEqual(1L + row.GetProperty("logIndexDelta").GetInt64(), receipt.GetProperty("logIndex").GetInt64());
                AssertEqual(row.GetProperty("eventId").GetString() == "match" ? eventId : Guid.Empty,
                    Guid.ParseExact(receipt.GetProperty("eventId").GetString()!, "N"));
                var expectedHash = row.GetProperty("eventHash").GetString() == "match"
                    ? CanonicalProtocol.ComputeEventHashBytes(Convert.FromHexString(Fixtures.ExpectedSeedHash), ReadFixture("hold.event.json")) : new byte[32];
                AssertTrue(expectedHash.SequenceEqual(Convert.FromHexString(receipt.GetProperty("eventHash").GetString()!)));
                AssertEqual(row.GetProperty("recoveryRunId").GetString(), receipt.GetProperty("recoveryRunId").GetString());
            });
            await RunTestAsync("Q1/Q4 handoff receipt: " + row.GetProperty("name").GetString(), async () =>
            {
                var store = new FakeBookingStore();
                var gate = new FakeAuthorityGate();
                var replication = new FakeReplicationPipeline
                {
                    ReceiptFactory = e =>
                    {
                        // Rebase the immutable receipt vector to the event created by the fake store.
                        var hash = row.GetProperty("eventHash").GetString() == "match" ? e.EventHash : new byte[32];
                        return new AppliedReceipt(row.GetProperty("nodeId").GetInt32(),
                            e.Epoch + row.GetProperty("authorityEpochDelta").GetInt64(),
                            e.Epoch + row.GetProperty("eventEpochDelta").GetInt64(),
                            e.LogIndex + row.GetProperty("logIndexDelta").GetInt64(),
                            row.GetProperty("eventId").GetString() == "match" ? e.EventId : Guid.Empty,
                            hash, row.GetProperty("recoveryRunId").ValueKind == JsonValueKind.Null ? null : requestId);
                    }
                };
                await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
                var reply = await coordinator.SubmitAsync(command, CancellationToken.None);
                var valid = row.GetProperty("valid").GetBoolean();
                AssertEqual(valid ? TransportStatus.Final : TransportStatus.Unknown, reply.Status);
                AssertEqual(valid ? 1L : 0L, store.GetStableIndex());
                AssertEqual(valid, gate.CanServe);
            });
        }

        foreach (var action in new[] { BookingAction.Confirm, BookingAction.Cancel })
        {
            foreach (var scenario in new[] { "missing", "wrong-owner", "wrong-seat", "cancelled" })
            {
                RunTest($"Q2 table: {action}/{scenario} has no projection patch", () =>
                {
                    var reservation = new ReservationRecord(reservationId, 1, scenario == "wrong-seat" ? 2 : 1,
                        scenario == "wrong-owner" ? "B" : "A", ReservationStatus.Cancelled, utc, 1);
                    var d = BookingDecisionEngine.Decide(new CommandData(action, 1, 1, reservationId), "A",
                        new SeatView(1, 1, SeatStatus.Available, null, null, 2),
                        scenario == "missing" ? null : reservation, null, 3, 1, 1, utc);
                    var expected = scenario switch
                    {
                        "missing" => ResultCodes.ReservationNotFound,
                        "wrong-owner" => ResultCodes.NotOwner,
                        "wrong-seat" => ResultCodes.ReservationSeatMismatch,
                        _ => action == BookingAction.Confirm ? ResultCodes.ReservationCancelled : ResultCodes.AlreadyCancelled
                    };
                    AssertEqual(expected, d.Result.Code);
                    AssertEqual(DecisionKind.NoChange, d.Kind);
                    AssertNull(d.SeatAfter);
                    AssertNull(d.ReservationAfter);
                    AssertNull(d.TicketCreated);
                    if (scenario == "wrong-owner") { AssertNull(d.Result.ReservationId); AssertNull(d.Result.TicketId); }
                });
            }
        }

        await RunTestAsync("Q3 retry: five concurrent identical commands create one immutable event", async () =>
        {
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, new FakeReplicationPipeline());
            var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => coordinator.SubmitAsync(command, CancellationToken.None)));
            AssertEqual(1, store.AppliedEvents.Count);
            foreach (var reply in replies) { AssertEqual(TransportStatus.Final, reply.Status); AssertEqual(replies[0], reply); }
            AssertEqual(1, store.Reservations.Count);
        });

        await RunTestAsync("Q3 retry: LocalApplied duplicate remains UNKNOWN and creates no event", async () =>
        {
            var store = new FakeBookingStore();
            store.Requests[requestId] = new ExistingRequestRecord(requestId, "A", command.Identity.CommandHash, 1, result, false);
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, new FakeReplicationPipeline());
            var reply = await coordinator.SubmitAsync(command, CancellationToken.None);
            AssertEqual(TransportStatus.Unknown, reply.Status);
            AssertNull(reply.Result);
            AssertEqual(0, store.AppliedEvents.Count);
        });

        await RunTestAsync("Q3 retry: stable SEAT_BUSY survives cancellation; new RequestId can HOLD", async () =>
        {
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, new FakeReplicationPipeline());
            var held = await coordinator.SubmitAsync(command, CancellationToken.None);
            var busyCommand = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 1);
            var busy = await coordinator.SubmitAsync(busyCommand, CancellationToken.None);
            AssertEqual(ResultCodes.SeatBusy, busy.Result?.Code);
            AssertEqual(TransportStatus.Final, busy.Status);
            var oldEventBytes = store.AppliedEvents[1].PayloadBytes.ToArray();
            var oldResultBytes = store.AppliedEvents[1].ResultPayload.ToArray();
            var cancel = Fixtures.CreateCancelCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1, held.Result!.ReservationId!.Value);
            AssertEqual(ResultCodes.OkCancelled, (await coordinator.SubmitAsync(cancel, CancellationToken.None)).Result?.Code);
            AssertEqual(busy, await coordinator.SubmitAsync(busyCommand, CancellationToken.None));
            AssertEqual(3, store.AppliedEvents.Count);
            AssertTrue(oldEventBytes.SequenceEqual(store.AppliedEvents[1].PayloadBytes.ToArray()));
            AssertTrue(oldResultBytes.SequenceEqual(store.AppliedEvents[1].ResultPayload.ToArray()));
            var fresh = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 1);
            AssertEqual(ResultCodes.OkHeld, (await coordinator.SubmitAsync(fresh, CancellationToken.None)).Result?.Code);
            using var busyPayload = JsonDocument.Parse(oldEventBytes);
            AssertEqual("NO_CHANGE", busyPayload.RootElement.GetProperty("decision").GetProperty("kind").GetString());
            AssertEqual(JsonValueKind.Null, busyPayload.RootElement.GetProperty("decision").GetProperty("seatAfter").ValueKind);
            AssertEqual(1L, busyPayload.RootElement.GetProperty("beforeSeatVersion").GetInt64());
        });

        await RunTestAsync("Q2/Q3: CONFIRM retry preserves ticket; new RequestId reports ALREADY_CONFIRMED", async () =>
        {
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, new FakeReplicationPipeline());
            var held = await coordinator.SubmitAsync(command, CancellationToken.None);
            var id = held.Result!.ReservationId!.Value;
            var confirm = Fixtures.CreateConfirmCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1, id);
            var sold = await coordinator.SubmitAsync(confirm, CancellationToken.None);
            AssertEqual(ResultCodes.OkSold, sold.Result?.Code);
            AssertEqual(sold, await coordinator.SubmitAsync(confirm, CancellationToken.None));
            AssertEqual(2, store.AppliedEvents.Count);
            var confirmed = await coordinator.SubmitAsync(Fixtures.CreateConfirmCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1, id), CancellationToken.None);
            AssertEqual(ResultCodes.AlreadyConfirmed, confirmed.Result?.Code);
            AssertEqual(sold.Result!.TicketId, confirmed.Result!.TicketId);
            AssertEqual(1, store.Tickets.Count);
            AssertEqual(3, store.AppliedEvents.Count);
            var denied = await coordinator.SubmitAsync(Fixtures.CreateCancelCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1, id), CancellationToken.None);
            AssertEqual(ResultCodes.TicketNotCancellable, denied.Result?.Code);
            AssertEqual(SeatStatus.Sold, store.Seats[1].Status);
        });
    }
}
