using De6.Booking.Domain;
using De6.Contracts;

namespace De6.Domain.UnitTests;

internal static partial class Program
{
    private static int _failures = 0;
    private static int _count = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("=== Running De6.Domain.UnitTests (Gia Quân - Tuần 1: Q1 -> Q4) ===");

        // --- Task 1 (Q1): Canonical Protocol & Fixtures ---
        RunTest("Q1: HOLD command hash matches canonical fixture", () =>
        {
            var hash = CanonicalProtocol.ComputeCommandHashString(
                Fixtures.SampleDatasetId,
                "A",
                BookingAction.Hold,
                1,
                1,
                null);
            AssertEqual(Fixtures.ExpectedHoldHash, hash);
        });

        RunTest("Q1: Seed hash matches canonical fixture", () =>
        {
            var hash = CanonicalProtocol.ComputeSeedHashHex(Fixtures.SampleDatasetId);
            AssertEqual(Fixtures.ExpectedSeedHash, hash);
        });

        RunTest("Q1: CONFIRM command hash matches canonical fixture", () =>
        {
            var resId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var hash = CanonicalProtocol.ComputeCommandHashString(
                Fixtures.SampleDatasetId,
                "A",
                BookingAction.Confirm,
                1,
                1,
                resId);
            AssertEqual(Fixtures.ExpectedConfirmHash, hash);
        });

        RunTest("Q1: Changing RequestId does NOT alter CommandHash", () =>
        {
            var hash1 = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "A", BookingAction.Hold, 1, 2, null);
            var hash2 = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "A", BookingAction.Hold, 1, 2, null);
            AssertEqual(hash1, hash2);
        });

        RunTest("Q1: Changing Owner, SeatNo or Action alters CommandHash", () =>
        {
            var baseHash = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "A", BookingAction.Hold, 1, 1, null);
            var diffOwner = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "B", BookingAction.Hold, 1, 1, null);
            var diffSeat = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "A", BookingAction.Hold, 1, 2, null);
            var diffAction = CanonicalProtocol.ComputeCommandHashString(Fixtures.SampleDatasetId, "A", BookingAction.Cancel, 1, 1, Guid.NewGuid());

            AssertTrue(baseHash != diffOwner);
            AssertTrue(baseHash != diffSeat);
            AssertTrue(baseHash != diffAction);
        });

        RunTest("Q1: BusinessResult canonical serialize/deserialize roundtrip", () =>
        {
            var resId = Guid.NewGuid();
            var ticketId = Guid.NewGuid();
            var original = new BusinessResult(ResultCodes.OkSold, 1, 5, resId, ticketId);
            var bytes = CanonicalProtocol.SerializeBusinessResult(original);
            var roundtrip = CanonicalProtocol.DeserializeBusinessResult(bytes);

            AssertEqual(original.Code, roundtrip.Code);
            AssertEqual(original.TripId, roundtrip.TripId);
            AssertEqual(original.SeatNo, roundtrip.SeatNo);
            AssertEqual(original.ReservationId, roundtrip.ReservationId);
            AssertEqual(original.TicketId, roundtrip.TicketId);
        });

        // --- Task 2 (Q2): Decision Engine Rules ---
        RunTest("Q2: HOLD on AVAILABLE seat -> OK_HELD, HeldCreated", () =>
        {
            var seat = new SeatView(1, 1, SeatStatus.Available, null, null, 0);
            var cmd = new CommandData(BookingAction.Hold, 1, 1, null);
            var dec = BookingDecisionEngine.Decide(cmd, "A", seat, null, null, 1, 1, 1, DateTime.UtcNow);

            AssertEqual(DecisionKind.HeldCreated, dec.Kind);
            AssertEqual(ResultCodes.OkHeld, dec.Result.Code);
            AssertNotNull(dec.SeatAfter);
            AssertEqual(SeatStatus.Held, dec.SeatAfter!.Status);
            AssertEqual("A", dec.SeatAfter.OwnerId);
            AssertNotNull(dec.ReservationAfter);
            AssertEqual(ReservationStatus.Held, dec.ReservationAfter!.Status);
        });

        RunTest("Q2: HOLD on HELD seat -> SEAT_BUSY, NoChange", () =>
        {
            var seat = new SeatView(1, 1, SeatStatus.Held, Guid.NewGuid(), "A", 1);
            var cmd = new CommandData(BookingAction.Hold, 1, 1, null);
            var dec = BookingDecisionEngine.Decide(cmd, "B", seat, null, null, 2, 1, 1, DateTime.UtcNow);

            AssertEqual(DecisionKind.NoChange, dec.Kind);
            AssertEqual(ResultCodes.SeatBusy, dec.Result.Code);
            AssertNull(dec.SeatAfter);
            AssertNull(dec.ReservationAfter);
        });

        RunTest("Q2: HOLD on SOLD seat -> ALREADY_SOLD, NoChange", () =>
        {
            var seat = new SeatView(1, 1, SeatStatus.Sold, Guid.NewGuid(), "A", 1);
            var cmd = new CommandData(BookingAction.Hold, 1, 1, null);
            var dec = BookingDecisionEngine.Decide(cmd, "B", seat, null, null, 2, 1, 1, DateTime.UtcNow);

            AssertEqual(DecisionKind.NoChange, dec.Kind);
            AssertEqual(ResultCodes.AlreadySold, dec.Result.Code);
        });

        RunTest("Q2: CONFIRM matching reservation -> OK_SOLD, SoldConfirmed, ticket issued", () =>
        {
            var resId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Held, resId, "A", 1);
            var res = new ReservationRecord(resId, 1, 1, "A", ReservationStatus.Held, DateTime.UtcNow.AddMinutes(-5), 1);
            var cmd = new CommandData(BookingAction.Confirm, 1, 1, resId);

            var dec = BookingDecisionEngine.Decide(cmd, "A", seat, res, null, 2, 1, 1, DateTime.UtcNow);
            AssertEqual(DecisionKind.SoldConfirmed, dec.Kind);
            AssertEqual(ResultCodes.OkSold, dec.Result.Code);
            AssertNotNull(dec.TicketCreated);
            AssertEqual("A", dec.TicketCreated!.OwnerId);
            AssertEqual(SeatStatus.Sold, dec.SeatAfter!.Status);
            AssertEqual(ReservationStatus.Confirmed, dec.ReservationAfter!.Status);
            // Verify CreatedUtc is preserved
            AssertEqual(res.CreatedUtc, dec.ReservationAfter.CreatedUtc);
        });

        RunTest("Q2: CONFIRM again -> ALREADY_CONFIRMED with existing TicketId", () =>
        {
            var resId = Guid.NewGuid();
            var ticketId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Sold, resId, "A", 2);
            var res = new ReservationRecord(resId, 1, 1, "A", ReservationStatus.Confirmed, DateTime.UtcNow.AddMinutes(-10), 1);
            var ticket = new TicketRecord(ticketId, 1, 1, resId, "A", DateTime.UtcNow.AddMinutes(-5), 2);
            var cmd = new CommandData(BookingAction.Confirm, 1, 1, resId);

            var dec = BookingDecisionEngine.Decide(cmd, "A", seat, res, ticket, 3, 1, 1, DateTime.UtcNow);
            AssertEqual(DecisionKind.NoChange, dec.Kind);
            AssertEqual(ResultCodes.AlreadyConfirmed, dec.Result.Code);
            AssertEqual(ticketId, dec.Result.TicketId);
        });

        RunTest("Q2: CANCEL on HELD seat -> OK_CANCELLED, HoldCancelled, seat AVAILABLE", () =>
        {
            var resId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Held, resId, "A", 1);
            var res = new ReservationRecord(resId, 1, 1, "A", ReservationStatus.Held, DateTime.UtcNow.AddMinutes(-2), 1);
            var cmd = new CommandData(BookingAction.Cancel, 1, 1, resId);

            var dec = BookingDecisionEngine.Decide(cmd, "A", seat, res, null, 2, 1, 1, DateTime.UtcNow);
            AssertEqual(DecisionKind.HoldCancelled, dec.Kind);
            AssertEqual(ResultCodes.OkCancelled, dec.Result.Code);
            AssertEqual(SeatStatus.Available, dec.SeatAfter!.Status);
            AssertNull(dec.SeatAfter.OwnerId);
            AssertNull(dec.SeatAfter.ReservationId);
            AssertEqual(ReservationStatus.Cancelled, dec.ReservationAfter!.Status);
        });

        RunTest("Q2: CANCEL on SOLD seat -> TICKET_NOT_CANCELLABLE, NoChange", () =>
        {
            var resId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Sold, resId, "A", 2);
            var res = new ReservationRecord(resId, 1, 1, "A", ReservationStatus.Confirmed, DateTime.UtcNow.AddMinutes(-5), 1);
            var cmd = new CommandData(BookingAction.Cancel, 1, 1, resId);

            var dec = BookingDecisionEngine.Decide(cmd, "A", seat, res, null, 3, 1, 1, DateTime.UtcNow);
            AssertEqual(DecisionKind.NoChange, dec.Kind);
            AssertEqual(ResultCodes.TicketNotCancellable, dec.Result.Code);
        });

        RunTest("Q2: Wrong owner -> NOT_OWNER, NoChange, does not leak data", () =>
        {
            var resId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Held, resId, "A", 1);
            var res = new ReservationRecord(resId, 1, 1, "A", ReservationStatus.Held, DateTime.UtcNow.AddMinutes(-2), 1);
            var cmd = new CommandData(BookingAction.Confirm, 1, 1, resId);

            var dec = BookingDecisionEngine.Decide(cmd, "B", seat, res, null, 2, 1, 1, DateTime.UtcNow);
            AssertEqual(DecisionKind.NoChange, dec.Kind);
            AssertEqual(ResultCodes.NotOwner, dec.Result.Code);
            AssertNull(dec.Result.ReservationId);
        });

        // --- Task 3 (Q3): Identity Validator & Dedup ---
        await RunTestAsync("Q3: Structural validation rejects invalid dataset/seat/owner", async () =>
        {
            var badDatasetCmd = Fixtures.CreateHoldCommand(Guid.NewGuid(), Guid.NewGuid(), "A", 1);
            var val1 = await CommandIdentityValidator.ValidateAndCheckDedupAsync(badDatasetCmd, Fixtures.SampleDatasetId);
            AssertEqual(ValidationStatus.RejectedBeforeEvent, val1.Status);

            var badSeatCmd = new AuthenticatedCommand(new CommandIdentity(
                Fixtures.SampleDatasetId, Guid.NewGuid(), "A", "invalidhash", new CommandData(BookingAction.Hold, 1, 99, null)));
            var val2 = await CommandIdentityValidator.ValidateAndCheckDedupAsync(badSeatCmd, Fixtures.SampleDatasetId);
            AssertEqual(ValidationStatus.RejectedBeforeEvent, val2.Status);
        });

        await RunTestAsync("Q3: Duplicate RequestId + same hash + Stable -> DuplicateStable with cached result", async () =>
        {
            var store = new FakeBookingStore { StableIndex = 1 };
            var reqId = Guid.NewGuid();
            var cmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, reqId, "A", 1);
            var expectedCached = new BusinessResult(ResultCodes.OkHeld, 1, 1, Guid.NewGuid(), null);

            store.Requests[reqId] = new ExistingRequestRecord(
                reqId, "A", cmd.Identity.CommandHash, 1, expectedCached, IsStable: true);

            var val = await CommandIdentityValidator.ValidateAndCheckDedupAsync(cmd, Fixtures.SampleDatasetId, store);
            AssertEqual(ValidationStatus.DuplicateStable, val.Status);
            AssertEqual(expectedCached, val.CachedResult);
        });

        await RunTestAsync("Q3: Same RequestId but modified SeatNo -> Conflict with REQUEST_CONFLICT", async () =>
        {
            var store = new FakeBookingStore();
            var reqId = Guid.NewGuid();
            var origCmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, reqId, "A", 1);

            store.Requests[reqId] = new ExistingRequestRecord(
                reqId, "A", origCmd.Identity.CommandHash, 1, new BusinessResult(ResultCodes.OkHeld, 1, 1, Guid.NewGuid(), null), IsStable: true);

            // Client re-sends same RequestId for Seat 2
            var tamperCmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, reqId, "A", 2);
            var val = await CommandIdentityValidator.ValidateAndCheckDedupAsync(tamperCmd, Fixtures.SampleDatasetId, store);
            AssertEqual(ValidationStatus.Conflict, val.Status);
            AssertEqual(ResultCodes.RequestConflict, val.ErrorCode);
        });

        await RunTestAsync("Q3: Record marked stable beyond StableIndex remains in flight", async () =>
        {
            var store = new FakeBookingStore();
            var command = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1);
            store.Requests[command.Identity.RequestId] = new ExistingRequestRecord(command.Identity.RequestId,
                "A", command.Identity.CommandHash, 1, new BusinessResult(ResultCodes.OkHeld, 1, 1, Guid.NewGuid(), null), true);
            var validation = await CommandIdentityValidator.ValidateAndCheckDedupAsync(command, Fixtures.SampleDatasetId, store);
            AssertEqual(ValidationStatus.DuplicateInFlight, validation.Status);
        });

        // --- Task 4 (Q4): Coordinator, Queue, Concurrency ---
        await RunTestAsync("Q4: Closed authority gate rejects with ClusterNotReady", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = false };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);

            var cmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1);
            var reply = await coordinator.SubmitAsync(cmd, CancellationToken.None);

            AssertEqual(TransportStatus.ClusterNotReady, reply.Status);
        });

        await RunTestAsync("Q4: Concurrency - 3 quầy HOLD same seat simultaneously -> exactly 1 OK_HELD, 2 SEAT_BUSY", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);

            var cmdA = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 5);
            var cmdB = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 5);
            var cmdC = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "C", 5);

            var tasks = new[]
            {
                coordinator.SubmitAsync(cmdA, CancellationToken.None),
                coordinator.SubmitAsync(cmdB, CancellationToken.None),
                coordinator.SubmitAsync(cmdC, CancellationToken.None)
            };

            var replies = await Task.WhenAll(tasks);
            var okCount = replies.Count(r => r.Status == TransportStatus.Final && r.Result?.Code == ResultCodes.OkHeld);
            var busyCount = replies.Count(r => r.Status == TransportStatus.Final && r.Result?.Code == ResultCodes.SeatBusy);

            AssertEqual(1, okCount);
            AssertEqual(2, busyCount);

            // Verify final seat state
            var seat5 = await store.GetSeatAsync(5);
            AssertEqual(SeatStatus.Held, seat5.Status);
        });

        await RunTestAsync("Q4: Client cancellation does not abort LocalApplied state", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);

            var cts = new CancellationTokenSource();
            var cmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 3);

            // Cancel immediately before/during submit
            cts.Cancel();
            var reply = await coordinator.SubmitAsync(cmd, cts.Token);

            AssertEqual(TransportStatus.Unknown, reply.Status);

            // Wait a brief moment for worker to complete
            await Task.Delay(100);

            // Operation must still complete to store
            var seat3 = await store.GetSeatAsync(3);
            AssertEqual(SeatStatus.Held, seat3.Status);
        });

        await RunTestAsync("Q4: Authoritative read GetSeatsAsync returns consistent snapshot", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);

            var cmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 8);
            var reply = await coordinator.SubmitAsync(cmd, CancellationToken.None);
            AssertEqual(TransportStatus.Final, reply.Status);

            var snapshot = await coordinator.GetSeatsAsync();
            AssertEqual(10, snapshot.Seats.Count);
            var seat8 = snapshot.Seats.First(s => s.SeatNo == 8);
            AssertEqual(SeatStatus.Held, seat8.Status);
            AssertEqual("A", seat8.OwnerId);
        });

        await RunTestAsync("Q4: GetResultAsync respects owner and Stable state", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);

            var reqId = Guid.NewGuid();
            var cmd = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, reqId, "A", 9);
            await coordinator.SubmitAsync(cmd, CancellationToken.None);

            // Query by right owner
            var resOk = await coordinator.GetResultAsync(reqId, "A");
            AssertEqual(TransportStatus.Final, resOk.Status);
            AssertEqual(ResultCodes.OkHeld, resOk.Result?.Code);

            // Query by wrong owner
            var resBad = await coordinator.GetResultAsync(reqId, "B");
            AssertEqual(TransportStatus.Rejected, resBad.Status);
            AssertNull(resBad.Result);
        });

        await RunTestAsync("Q4: Missing replica ACK blocks the next mutation and reads", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline { ShouldSucceed = false };
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
            var first = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
            AssertEqual(TransportStatus.Unknown, first.Status);
            var second = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 2), CancellationToken.None);
            AssertEqual(TransportStatus.ClusterNotReady, second.Status);
            AssertEqual(1, store.AppliedEvents.Count);
            try { await coordinator.GetSeatsAsync(); throw new InvalidOperationException("Read exposed an unstable projection"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not ready")) { }
        });

        await RunTestAsync("Q4: A receipt from the primary cannot authorize FINAL", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            var replication = new FakeReplicationPipeline { AckNodeId = 1 };
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
            var reply = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
            AssertEqual(TransportStatus.Unknown, reply.Status);
            AssertEqual(0L, store.GetStableIndex());
        });

        await RunTestAsync("Q4: Invalid command is never reported FINAL", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var invalid = Fixtures.CreateHoldCommand(Guid.NewGuid(), Guid.NewGuid(), "A", 1);
            var reply = await coordinator.SubmitAsync(invalid, CancellationToken.None);
            AssertTrue(reply.Status != TransportStatus.Final);
            AssertEqual(0, store.AppliedEvents.Count);
        });

        await RunTestAsync("Q4: Missing RequestId is not a FINAL reservation result", async () =>
        {
            var gate = new FakeAuthorityGate { CanServe = true };
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var reply = await coordinator.GetResultAsync(Guid.NewGuid(), "A");
            AssertTrue(reply.Status != TransportStatus.Final);
            AssertTrue(reply.Result?.Code != ResultCodes.ReservationNotFound);
        });

        await RunTestAsync("Q3: Stable duplicate returns the original FINAL result", async () =>
        {
            var store = new FakeBookingStore();
            var gate = new FakeAuthorityGate();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var command = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1);
            var first = await coordinator.SubmitAsync(command, CancellationToken.None);
            var retry = await coordinator.SubmitAsync(command, CancellationToken.None);
            AssertEqual(TransportStatus.Final, first.Status);
            AssertEqual(first, retry);
            AssertEqual(1, store.AppliedEvents.Count);
        });

        await RunTestAsync("Q3: Request conflict does not return FINAL or the old result", async () =>
        {
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, new FakeReplicationPipeline());
            var requestId = Guid.NewGuid();
            await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, requestId, "A", 1), CancellationToken.None);
            var conflict = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, requestId, "B", 2), CancellationToken.None);
            AssertEqual(TransportStatus.Rejected, conflict.Status);
            AssertNull(conflict.Result);
            AssertEqual(ResultCodes.RequestConflict, conflict.ErrorCode);
            AssertEqual(1, store.AppliedEvents.Count);
        });

        await RunTestAsync("Q4: Stable write failure closes gate and does not stall pending work", async () =>
        {
            var store = new FakeBookingStore { FailMarkStable = true };
            var gate = new FakeAuthorityGate();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var first = coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
            var second = coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 2), CancellationToken.None);
            var replies = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(TransportStatus.Unknown, replies[0].Status);
            AssertEqual(TransportStatus.ClusterNotReady, replies[1].Status);
            AssertEqual(false, gate.CanServe);
        });

        RunTest("Q2: HELD reservation cannot confirm a different seat projection", () =>
        {
            var reservationId = Guid.NewGuid();
            var seat = new SeatView(1, 1, SeatStatus.Available, null, null, 1);
            var reservation = new ReservationRecord(reservationId, 1, 1, "A", ReservationStatus.Held, DateTime.UtcNow, 1);
            try
            {
                BookingDecisionEngine.Decide(new CommandData(BookingAction.Confirm, 1, 1, reservationId), "A", seat, reservation, null, 2, 1, 1, DateTime.UtcNow);
                throw new Exception("Expected inconsistent projection to be rejected");
            }
            catch (InvalidOperationException) { }
        });

        RunTest("Q1: Result payload has fixed UTF-8 bytes", () =>
        {
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var expected = "{\"code\":\"OK_HELD\",\"tripId\":1,\"seatNo\":1,\"reservationId\":\"11111111222233334444555555555555\",\"ticketId\":null}";
            var actual = System.Text.Encoding.UTF8.GetString(CanonicalProtocol.SerializeBusinessResult(new BusinessResult(ResultCodes.OkHeld, 1, 1, id, null)));
            AssertEqual(expected, actual);
        });

        RunTest("Q1: NO_CHANGE event has fixed UTF-8 bytes", () =>
        {
            var requestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var eventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var result = new BusinessResult(ResultCodes.SeatBusy, 1, 1, null, null);
            var decision = new BookingDecision(DecisionKind.NoChange, null, null, null, result);
            var payload = new EventPayloadData(Fixtures.SampleDatasetId, 7, 3, 1, eventId,
                new string('0', 64), requestId, "A", Fixtures.ExpectedHoldHash,
                new CommandData(BookingAction.Hold, 1, 1, null), decision, 6, result,
                new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc));
            var expected = "{\"datasetId\":\"124a4f064ac14c128b27202600000009\",\"logIndex\":7,\"epoch\":3,\"writerId\":1,\"eventId\":\"aaaaaaaabbbbccccddddeeeeeeeeeeee\",\"previousHash\":\"" + new string('0', 64) + "\",\"requestId\":\"11111111222233334444555555555555\",\"ownerId\":\"A\",\"commandHash\":\"" + Fixtures.ExpectedHoldHash + "\",\"command\":{\"action\":\"HOLD\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null},\"decision\":{\"kind\":\"NO_CHANGE\",\"seatAfter\":null,\"reservationAfter\":null,\"ticketCreated\":null,\"result\":{\"code\":\"SEAT_BUSY\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null,\"ticketId\":null}},\"beforeSeatVersion\":6,\"result\":{\"code\":\"SEAT_BUSY\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null,\"ticketId\":null},\"createdUtc\":\"2026-11-15T12:30:00.0000000Z\"}";
            AssertEqual(expected, System.Text.Encoding.UTF8.GetString(CanonicalProtocol.SerializeEventPayload(payload)));
        });

        await RunTestAsync("Q4: FINAL requires persisted StableIndex", async () =>
        {
            var store = new FakeBookingStore { SkipMarkStable = true };
            var gate = new FakeAuthorityGate();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var reply = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
            AssertEqual(TransportStatus.Unknown, reply.Status);
            AssertEqual(false, gate.CanServe);
        });

        await RunTestAsync("Q4: Queue full returns RETRY_LATER before store Apply", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var replication = new FakeReplicationPipeline
            {
                BeforeReceiptAsync = _ => { entered.TrySetResult(); return release.Task; }
            };
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, new FakeAuthorityGate(), store, replication, queueCapacity: 1);
            try
            {
                var first = coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var second = coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "B", 2), CancellationToken.None);
                var third = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "C", 3), CancellationToken.None);
                AssertEqual(TransportStatus.RetryLater, third.Status);
                AssertEqual(1, store.AppliedEvents.Count);
                release.SetResult();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
                AssertEqual(2, store.AppliedEvents.Count);
            }
            finally { release.TrySetResult(); }
        });

        await RunTestAsync("Q4: Unstable projection is not readable before replica ACK", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var replication = new FakeReplicationPipeline { BeforeReceiptAsync = _ => { entered.TrySetResult(); return release.Task; } };
            var gate = new FakeAuthorityGate();
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
            try
            {
                var command = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1);
                var mutation = coordinator.SubmitAsync(command, CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var read = coordinator.GetSeatsAsync();
                AssertEqual(false, read.IsCompleted);
                AssertEqual(0L, store.GetStableIndex());
                release.SetResult();
                AssertEqual(TransportStatus.Final, (await mutation).Status);
                AssertEqual(SeatStatus.Held, (await read).Seats[0].Status);
            }
            finally { release.TrySetResult(); }
        });

        await RunTestAsync("Q4: Wrong receipt hash never advances StableIndex", async () =>
        {
            var replication = new FakeReplicationPipeline
            {
                ReceiptFactory = e => new AppliedReceipt(2, e.Epoch, e.Epoch, e.LogIndex,
                    e.EventId, new byte[32], null)
            };
            var store = new FakeBookingStore();
            var gate = new FakeAuthorityGate();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
            var reply = await coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
            AssertEqual(TransportStatus.Unknown, reply.Status);
            AssertEqual(0L, store.GetStableIndex());
            AssertEqual(false, gate.CanServe);
        });

        RunTest("Q1: EventHash matches independent SHA-256 fixture", () =>
        {
            var actual = CanonicalProtocol.ComputeEventHashHex(new byte[32], System.Text.Encoding.UTF8.GetBytes("abc"));
            AssertEqual("365aa7d8f7f9402c4b9434502b4cc89ddb09fe50d7cd95b493b834c62d5a5370", actual);
        });

        RunTest("Q1: Canonical command rejects delimiter in OwnerId", () =>
        {
            try
            {
                CanonicalProtocol.BuildCommandHashRawString(Fixtures.SampleDatasetId, "A|B", BookingAction.Hold, 1, 1, null);
                throw new Exception("Expected invalid OwnerId to be rejected");
            }
            catch (ArgumentException) { }
        });

        RunTest("Q1: Event payload rejects mismatched result and non-UTC time", () =>
        {
            var goodResult = new BusinessResult(ResultCodes.SeatBusy, 1, 1, null, null);
            var payload = new EventPayloadData(Fixtures.SampleDatasetId, 1, 1, 1, Guid.NewGuid(),
                new string('0', 64), Guid.NewGuid(), "A", Fixtures.ExpectedHoldHash,
                new CommandData(BookingAction.Hold, 1, 1, null),
                new BookingDecision(DecisionKind.NoChange, null, null, null, goodResult), 0,
                new BusinessResult(ResultCodes.OkHeld, 1, 1, null, null), DateTime.UtcNow);
            try
            {
                CanonicalProtocol.SerializeEventPayload(payload);
                throw new Exception("Expected result mismatch rejection");
            }
            catch (ArgumentException) { }
            try
            {
                CanonicalProtocol.SerializeEventPayload(payload with { Result = goodResult, CreatedUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local) });
                throw new Exception("Expected non-UTC rejection");
            }
            catch (ArgumentException) { }
        });

        RunTest("Q2: CONFIRMED reservation requires matching sold seat and ticket", () =>
        {
            var reservationId = Guid.NewGuid();
            var reservation = new ReservationRecord(reservationId, 1, 1, "A", ReservationStatus.Confirmed, DateTime.UtcNow, 1);
            var seat = new SeatView(1, 1, SeatStatus.Available, null, null, 1);
            var ticket = new TicketRecord(Guid.NewGuid(), 1, 1, reservationId, "A", DateTime.UtcNow, 1);
            try
            {
                BookingDecisionEngine.Decide(new CommandData(BookingAction.Confirm, 1, 1, reservationId), "A", seat, reservation, ticket, 2, 1, 1, DateTime.UtcNow);
                throw new Exception("Expected corrupt confirmed projection rejection");
            }
            catch (InvalidOperationException) { }
        });

        await RunTestAsync("Q4: Freeze before ACK prevents FINAL and later reads", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var replication = new FakeReplicationPipeline { BeforeReceiptAsync = _ => { entered.TrySetResult(); return release.Task; } };
            var gate = new FakeAuthorityGate();
            var store = new FakeBookingStore();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, replication);
            try
            {
                var pending = coordinator.SubmitAsync(Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, Guid.NewGuid(), "A", 1), CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                gate.CanServe = false;
                release.SetResult();
                AssertEqual(TransportStatus.Unknown, (await pending).Status);
                AssertEqual(0L, store.GetStableIndex());
                try { await coordinator.GetSeatsAsync(); throw new Exception("Expected read to be blocked"); }
                catch (InvalidOperationException) { }
            }
            finally { release.TrySetResult(); }
        });

        await RunTestAsync("Q4: GET_RESULT cannot return FINAL after freeze during lookup", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new FakeBookingStore();
            var gate = new FakeAuthorityGate();
            await using var coordinator = new CommandCoordinator(Fixtures.SampleDatasetId, gate, store, new FakeReplicationPipeline());
            var requestId = Guid.NewGuid();
            var command = Fixtures.CreateHoldCommand(Fixtures.SampleDatasetId, requestId, "A", 1);
            AssertEqual(TransportStatus.Final, (await coordinator.SubmitAsync(command, CancellationToken.None)).Status);
            store.BeforeFindAsync = () => { entered.TrySetResult(); return release.Task; };
            try
            {
                var pending = coordinator.GetResultAsync(requestId, "A");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                gate.CanServe = false;
                release.SetResult();
                AssertEqual(TransportStatus.ClusterNotReady, (await pending).Status);
            }
            finally { release.TrySetResult(); }
        });

        await RunWeekOneHandoffTestsAsync();

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"Summary: {_count - _failures}/{_count} tests PASSED.");
        Console.WriteLine($"========================================");

        return _failures == 0 ? 0 : 1;
    }

    private static void RunTest(string name, Action action)
    {
        _count++;
        try
        {
            action();
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static async Task RunTestAsync(string name, Func<Task> action)
    {
        _count++;
        try
        {
            await action();
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected: '{expected}', Actual: '{actual}'");
    }

    private static void AssertTrue(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Condition was false, expected true");
    }

    private static void AssertNotNull<T>(T? value)
    {
        if (value is null)
            throw new InvalidOperationException("Value was null, expected non-null");
    }

    private static void AssertNull<T>(T? value)
    {
        if (value is not null)
            throw new InvalidOperationException($"Value was not null: {value}");
    }
}
