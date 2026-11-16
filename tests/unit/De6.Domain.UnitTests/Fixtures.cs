using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using De6.Booking.Domain;
using De6.Cluster.Replication;
using De6.Contracts;

namespace De6.Domain.UnitTests;

public static class Fixtures
{
    public static readonly Guid SampleDatasetId = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    public const string ExpectedHoldHash = "4f83fa113fb5dfad9ef0a1990b0dc0ad31c5b16a9016509b93114bba5ea500b0";
    public const string ExpectedSeedHash = "a79c44cee6baf83e69fd2302e2a49210317de75ae15829b48eb14e21ac14c327";
    public const string ExpectedConfirmHash = "cbf5bde7c245ab352b06053b36788d0de9c04ff8b8ab30e8b43abb62c5a8e0ed";

    public static AuthenticatedCommand CreateHoldCommand(Guid datasetId, Guid requestId, string ownerId, int seatNo)
    {
        var cmd = new CommandData(BookingAction.Hold, 1, seatNo, null);
        var hash = CanonicalProtocol.ComputeCommandHashString(datasetId, ownerId, BookingAction.Hold, 1, seatNo, null);
        var identity = new CommandIdentity(datasetId, requestId, ownerId, hash, cmd);
        return new AuthenticatedCommand(identity);
    }

    public static AuthenticatedCommand CreateConfirmCommand(Guid datasetId, Guid requestId, string ownerId, int seatNo, Guid reservationId)
    {
        var cmd = new CommandData(BookingAction.Confirm, 1, seatNo, reservationId);
        var hash = CanonicalProtocol.ComputeCommandHashString(datasetId, ownerId, BookingAction.Confirm, 1, seatNo, reservationId);
        var identity = new CommandIdentity(datasetId, requestId, ownerId, hash, cmd);
        return new AuthenticatedCommand(identity);
    }

    public static AuthenticatedCommand CreateCancelCommand(Guid datasetId, Guid requestId, string ownerId, int seatNo, Guid reservationId)
    {
        var cmd = new CommandData(BookingAction.Cancel, 1, seatNo, reservationId);
        var hash = CanonicalProtocol.ComputeCommandHashString(datasetId, ownerId, BookingAction.Cancel, 1, seatNo, reservationId);
        var identity = new CommandIdentity(datasetId, requestId, ownerId, hash, cmd);
        return new AuthenticatedCommand(identity);
    }
}

public sealed class FakeAuthorityGate : IAuthorityGate
{
    public bool CanServe { get; set; } = true;
    public long CurrentEpoch { get; set; } = 1;
    public int LeaderId { get; set; } = 1;
    public void BlockUncertain() => CanServe = false;
}

public sealed class FakeBookingStore : De6.Booking.Domain.IBookingStore
{
    public readonly ConcurrentDictionary<int, SeatView> Seats = new();
    public readonly ConcurrentDictionary<Guid, ReservationRecord> Reservations = new();
    public readonly ConcurrentDictionary<Guid, TicketRecord> Tickets = new();
    public readonly ConcurrentDictionary<Guid, ExistingRequestRecord> Requests = new();
    public readonly List<PersistedEvent> AppliedEvents = new();
    public long StableIndex;
    public bool FailMarkStable { get; set; }
    public bool SkipMarkStable { get; set; }
    public Func<Task>? BeforeFindAsync { get; set; }

    public FakeBookingStore()
    {
        for (var i = 1; i <= 10; i++)
            Seats[i] = new SeatView(1, i, SeatStatus.Available, null, null, 0);
    }

    public ValueTask<SeatView> GetSeatAsync(int seatNo, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Seats[seatNo]);
    }

    public ValueTask<IReadOnlyList<SeatView>> GetAllSeatsAsync(CancellationToken cancellationToken = default)
    {
        var list = Seats.Values.OrderBy(s => s.SeatNo).ToList() as IReadOnlyList<SeatView>;
        return ValueTask.FromResult(list);
    }

    public ValueTask<ReservationRecord?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        Reservations.TryGetValue(reservationId, out var res);
        return ValueTask.FromResult<ReservationRecord?>(res);
    }

    public ValueTask<TicketRecord?> GetTicketByReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var ticket = Tickets.Values.FirstOrDefault(t => t.ReservationId == reservationId);
        return ValueTask.FromResult(ticket);
    }

    public async ValueTask<ExistingRequestRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (BeforeFindAsync is not null)
            await BeforeFindAsync();
        Requests.TryGetValue(requestId, out var req);
        return req;
    }

    public Task<PersistedEvent> ApplyLocalAsync(
        AuthenticatedCommand command,
        BookingDecision decision,
        long epoch,
        int writerId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var logIndex = AppliedEvents.Count + 1;
        var eventId = Guid.NewGuid();
        var beforeSeatVersion = Seats[command.Identity.Command.SeatNo].VersionIndex;

        // Update in-memory projection
        if (decision.SeatAfter is not null)
            Seats[decision.SeatAfter.SeatNo] = decision.SeatAfter;

        if (decision.ReservationAfter is not null)
            Reservations[decision.ReservationAfter.ReservationId] = decision.ReservationAfter;

        if (decision.TicketCreated is not null)
            Tickets[decision.TicketCreated.TicketId] = decision.TicketCreated;

        // Record request
        Requests[command.Identity.RequestId] = new ExistingRequestRecord(
            command.Identity.RequestId,
            command.Identity.OwnerId,
            command.Identity.CommandHash,
            logIndex,
            decision.Result,
            IsStable: false);

        var prevHash = AppliedEvents.Count == 0
            ? Fixtures.ExpectedSeedHash
            : Convert.ToHexString(AppliedEvents[^1].EventHash.ToArray()).ToLowerInvariant();

        var prevHashBytes = Convert.FromHexString(prevHash);

        var payloadData = new EventPayloadData(
            command.Identity.DatasetId,
            logIndex,
            epoch,
            writerId,
            eventId,
            prevHash,
            command.Identity.RequestId,
            command.Identity.OwnerId,
            command.Identity.CommandHash,
            command.Identity.Command,
            decision,
            beforeSeatVersion,
            decision.Result,
            utcNow);

        var payloadBytes = CanonicalProtocol.SerializeEventPayload(payloadData);
        var eventHashBytes = CanonicalProtocol.ComputeEventHashBytes(prevHashBytes, payloadBytes);
        var resultPayload = CanonicalProtocol.SerializeBusinessResult(decision.Result);

        var persisted = new PersistedEvent(
            command.Identity.DatasetId,
            logIndex,
            epoch,
            writerId,
            eventId,
            payloadBytes,
            eventHashBytes,
            resultPayload);

        AppliedEvents.Add(persisted);
        return Task.FromResult(persisted);
    }

    public Task MarkStableAsync(long logIndex, CancellationToken cancellationToken = default)
    {
        if (FailMarkStable) throw new IOException("Simulated stable write failure");
        if (SkipMarkStable) return Task.CompletedTask;
        StableIndex = logIndex;
        foreach (var (reqId, rec) in Requests)
        {
            if (rec.LogIndex <= logIndex)
            {
                Requests[reqId] = rec with { IsStable = true };
            }
        }
        return Task.CompletedTask;
    }

    public long GetStableIndex() => StableIndex;
}

public sealed class FakeReplicationPipeline : IReplicaSender
{
    public bool ShouldSucceed { get; set; } = true;
    public int AckNodeId { get; set; } = 2;
    public Action<PersistedEvent>? OnReplicate { get; set; }
    public Func<PersistedEvent, Task>? BeforeReceiptAsync { get; set; }
    public Func<PersistedEvent, AppliedReceipt?>? ReceiptFactory { get; set; }

    public async Task<ReplicationOutcome> EnsureOneBackupAsync(PersistedEvent persistedEvent, TimeSpan deadline, CancellationToken cancellationToken)
    {
        OnReplicate?.Invoke(persistedEvent);
        if (BeforeReceiptAsync is not null)
            await BeforeReceiptAsync(persistedEvent);
        if (!ShouldSucceed)
            return new ReplicationOutcome(null);

        return new ReplicationOutcome(ReceiptFactory?.Invoke(persistedEvent) ?? new AppliedReceipt(AckNodeId, persistedEvent.Epoch, persistedEvent.Epoch,
            persistedEvent.LogIndex, persistedEvent.EventId, persistedEvent.EventHash, null));
    }
}
