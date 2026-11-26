using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Simulation;

/// <summary>
/// FAKE shared state of the 10-seat trip, persisted in one JSON file so three client processes see the same cluster.
/// Implements the V12 section 7 decision table and RequestId idempotency. No replication or durability claims.
/// </summary>
public sealed class SimulatedCluster
{
    public const int PrimaryNodeId = 3;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _lockPath;
    private readonly Guid _datasetId;

    public SimulatedCluster(string directory, Guid datasetId)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "cluster.json");
        _lockPath = Path.Combine(directory, "cluster.lock");
        _datasetId = datasetId;
    }

    public Guid DatasetId => _datasetId;

    private sealed class Seat { public int SeatNo { get; set; } public string Status { get; set; } = "AVAILABLE"; public string? OwnerId { get; set; } public Guid? ReservationId { get; set; } public long Version { get; set; } }
    private sealed class Reservation { public Guid Id { get; set; } public int SeatNo { get; set; } public string OwnerId { get; set; } = ""; public string Status { get; set; } = "HELD"; public DateTime CreatedUtc { get; set; } public long UpdatedIndex { get; set; } public Guid? TicketId { get; set; } }
    private sealed class Stored { public Guid RequestId { get; set; } public string OwnerId { get; set; } = ""; public string CommandHash { get; set; } = ""; public string ResultJson { get; set; } = ""; }
    private sealed class State
    {
        public Guid DatasetId { get; set; }
        public long Epoch { get; set; } = 1;
        public long AppliedIndex { get; set; }
        public List<Seat> Seats { get; set; } = [];
        public List<Reservation> Reservations { get; set; } = [];
        public List<Stored> Results { get; set; } = [];
    }

    public long AppliedIndex => Run(s => s.AppliedIndex);
    public long Epoch => Run(s => s.Epoch);

    public StableSnapshot ReadSeats() => Run(s => new StableSnapshot(s.DatasetId, s.Epoch, s.AppliedIndex,
        s.Seats.OrderBy(x => x.SeatNo).Select(x => new SeatView(1, x.SeatNo, Enum.Parse<SeatStatus>(x.Status, true), x.ReservationId, x.OwnerId, x.Version)).ToArray()));

    public ReservationsSnapshot ReadReservations(string owner) => Run(s => new ReservationsSnapshot(s.AppliedIndex,
        s.Reservations.Where(r => r.OwnerId == owner).OrderBy(r => r.CreatedUtc)
            .Select(r => new ReservationData(r.Id, 1, r.SeatNo, r.OwnerId, Enum.Parse<ReservationStatus>(r.Status, true), r.CreatedUtc, r.UpdatedIndex)).ToArray()));

    /// <summary>GET_RESULT: only the owner of the RequestId may read it.</summary>
    public BusinessResult? GetResult(string owner, Guid requestId) => Run(s =>
    {
        Stored? stored = s.Results.FirstOrDefault(r => r.RequestId == requestId && r.OwnerId == owner);
        return stored is null ? null : CanonicalProtocol.DeserializeBusinessResult(System.Text.Encoding.UTF8.GetBytes(stored.ResultJson));
    });

    public BusinessResult Execute(string owner, Guid requestId, string commandHash, CommandData command) => Run(s =>
    {
        Stored? existing = s.Results.FirstOrDefault(r => r.RequestId == requestId);
        if (existing is not null)
        {
            if (existing.OwnerId != owner || !string.Equals(existing.CommandHash, commandHash, StringComparison.OrdinalIgnoreCase))
                return new BusinessResult(ResultCodes.RequestConflict, 1, command.SeatNo, null, null);
            return CanonicalProtocol.DeserializeBusinessResult(System.Text.Encoding.UTF8.GetBytes(existing.ResultJson));
        }
        BusinessResult result = Decide(s, owner, command);
        s.AppliedIndex++;
        s.Results.Add(new Stored { RequestId = requestId, OwnerId = owner, CommandHash = commandHash, ResultJson = System.Text.Encoding.UTF8.GetString(CanonicalProtocol.SerializeBusinessResult(result)) });
        return result;
    }, mutate: true);

    private static BusinessResult Decide(State s, string owner, CommandData c)
    {
        Seat? seat = s.Seats.FirstOrDefault(x => x.SeatNo == c.SeatNo);
        if (seat is null || c.TripId != 1) return new BusinessResult(ResultCodes.ReservationSeatMismatch, 1, c.SeatNo, null, null);
        long next = s.AppliedIndex + 1;
        if (c.Action == BookingAction.Hold)
        {
            if (seat.Status == "SOLD") return new BusinessResult(ResultCodes.AlreadySold, 1, c.SeatNo, null, null);
            if (seat.Status == "HELD") return new BusinessResult(ResultCodes.SeatBusy, 1, c.SeatNo, null, null);
            Reservation r = new() { Id = Guid.NewGuid(), SeatNo = c.SeatNo, OwnerId = owner, Status = "HELD", CreatedUtc = DateTime.UtcNow, UpdatedIndex = next };
            s.Reservations.Add(r);
            seat.Status = "HELD"; seat.OwnerId = owner; seat.ReservationId = r.Id; seat.Version = next;
            return new BusinessResult(ResultCodes.OkHeld, 1, c.SeatNo, r.Id, null);
        }
        Reservation? res = c.ReservationId is { } id ? s.Reservations.FirstOrDefault(x => x.Id == id) : null;
        if (res is null) return new BusinessResult(ResultCodes.ReservationNotFound, 1, c.SeatNo, null, null);
        if (res.OwnerId != owner) return new BusinessResult(ResultCodes.NotOwner, 1, c.SeatNo, null, null);
        if (res.SeatNo != c.SeatNo) return new BusinessResult(ResultCodes.ReservationSeatMismatch, 1, c.SeatNo, null, null);
        if (c.Action == BookingAction.Confirm)
        {
            if (res.Status == "CONFIRMED") return new BusinessResult(ResultCodes.AlreadyConfirmed, 1, c.SeatNo, res.Id, res.TicketId);
            if (res.Status == "CANCELLED") return new BusinessResult(ResultCodes.ReservationCancelled, 1, c.SeatNo, res.Id, null);
            res.Status = "CONFIRMED"; res.TicketId = Guid.NewGuid(); res.UpdatedIndex = next;
            seat.Status = "SOLD"; seat.Version = next;
            return new BusinessResult(ResultCodes.OkSold, 1, c.SeatNo, res.Id, res.TicketId);
        }
        if (res.Status == "CONFIRMED") return new BusinessResult(ResultCodes.TicketNotCancellable, 1, c.SeatNo, res.Id, null);
        if (res.Status == "CANCELLED") return new BusinessResult(ResultCodes.AlreadyCancelled, 1, c.SeatNo, res.Id, null);
        res.Status = "CANCELLED"; res.UpdatedIndex = next;
        seat.Status = "AVAILABLE"; seat.OwnerId = null; seat.ReservationId = null; seat.Version = next;
        return new BusinessResult(ResultCodes.OkCancelled, 1, c.SeatNo, res.Id, null);
    }

    private T Run<T>(Func<State, T> action, bool mutate = false)
    {
        using FileStream gate = AcquireLock();
        State state = File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? NewState() : NewState();
        if (state.DatasetId != _datasetId) throw new InvalidDataException("Simulated cluster file belongs to another DatasetId; delete data/demo to reset.");
        T value = action(state);
        if (mutate || !File.Exists(_path))
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, _path, overwrite: true);
        }
        return value;
    }

    private State NewState() => new()
    {
        DatasetId = _datasetId,
        Seats = Enumerable.Range(1, 10).Select(n => new Seat { SeatNo = n }).ToList()
    };

    private FileStream AcquireLock()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 200) { Thread.Sleep(10); }
        }
    }
}
