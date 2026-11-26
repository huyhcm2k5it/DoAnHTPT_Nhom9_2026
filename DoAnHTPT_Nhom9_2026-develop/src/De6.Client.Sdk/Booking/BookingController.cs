using System.Diagnostics;
using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

/// <summary>
/// UI-independent client state machine (V12 sections 7, 15, 17). Rules it enforces:
/// a success is shown only for a FINAL reply; pending/UNKNOWN commands are tracked apart from seat state;
/// the same RequestId is reused on every retry; cached data is flagged stale and never confirms a sale.
/// </summary>
public sealed class BookingController : IAsyncDisposable
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private const int TraceCapacity = 1000;

    private readonly IBookingGateway _gateway;
    private readonly string _ownerId;
    private readonly Guid _datasetId;
    private readonly int _clientId;
    private readonly string? _evidenceDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly Dictionary<Guid, PendingItem> _pending = [];
    private readonly List<TraceEntry> _trace = [];
    private readonly List<double> _latenciesMs = [];
    private readonly StreamWriter? _traceFile;
    private StableSnapshot? _board;
    private IReadOnlyList<ReservationData> _reservations = [];
    private DateTime? _boardReadUtc;
    private bool _stale;
    private LastResult? _last;
    private int _sent, _final, _unresolved, _rejected, _resolvedLater;
    private bool _disposed;

    public event Action? Changed;

    public BookingController(IBookingGateway gateway, string ownerId, Guid datasetId, int clientId, string? evidenceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        BookingCanonical.ValidateOwner(ownerId);
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));
        _gateway = gateway;
        _ownerId = ownerId;
        _datasetId = datasetId;
        _clientId = clientId;
        _evidenceDirectory = evidenceDirectory;
        if (evidenceDirectory is not null)
        {
            Directory.CreateDirectory(evidenceDirectory);
            string path = Path.Combine(evidenceDirectory, $"trace-c{clientId}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _traceFile = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
    }

    public string OwnerId => _ownerId;
    public int ClientId => _clientId;
    public ClientNodeStatus? NodeStatus => _gateway.ActiveStatus;
    public bool Busy => _gate.CurrentCount == 0;

    public StableSnapshot? Board { get { lock (_state) return _board; } }
    public DateTime? BoardReadUtc { get { lock (_state) return _boardReadUtc; } }
    public bool BoardIsStale { get { lock (_state) return _stale; } }
    public LastResult? Last { get { lock (_state) return _last; } }
    public IReadOnlyList<ReservationData> MyReservations { get { lock (_state) return _reservations; } }
    public IReadOnlyList<PendingItem> Pending { get { lock (_state) return _pending.Values.OrderBy(p => p.SavedUtc).ToArray(); } }
    public IReadOnlyList<TraceEntry> Trace { get { lock (_state) return _trace.ToArray(); } }

    public ClientMetrics Metrics
    {
        get
        {
            lock (_state)
            {
                double[] sorted = _latenciesMs.Order().ToArray();
                double avg = sorted.Length == 0 ? 0 : sorted.Average();
                double p95 = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
                double max = sorted.Length == 0 ? 0 : sorted[^1];
                return new ClientMetrics(_sent, _final, _unresolved, _rejected, _resolvedLater, avg, p95, max);
            }
        }
    }

    public IReadOnlyList<SeatCell> BuildCells()
    {
        lock (_state)
        {
            List<SeatCell> cells = [];
            for (int seatNo = 1; seatNo <= 10; seatNo++)
            {
                SeatView? seat = _board?.Seats.FirstOrDefault(s => s.SeatNo == seatNo);
                bool mine = seat is { Status: SeatStatus.Held } && StringComparer.Ordinal.Equals(seat.OwnerId, _ownerId);
                PendingItem? pending = _pending.Values.FirstOrDefault(p => p.SeatNo == seatNo);
                cells.Add(new SeatCell(seatNo, seat?.Status, mine, mine ? seat!.ReservationId : null, pending));
            }
            return cells;
        }
    }

    public SeatActions GetActions(int seatNo)
    {
        SeatCell? cell = BuildCells().FirstOrDefault(c => c.SeatNo == seatNo);
        if (cell is null || cell.Status is null || BoardIsStale || cell.Pending is not null) return SeatActions.None;
        return new SeatActions(
            CanHold: cell.Status == SeatStatus.Available,
            CanConfirm: cell.HeldByMe && cell.ReservationId is not null,
            CanCancel: cell.HeldByMe && cell.ReservationId is not null);
    }

    /// <summary>Startup path (K6): restore durable pending commands, resolve them, then read the authoritative board.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Log("INFO", $"Quầy C{_clientId} owner={_ownerId} dataset={_datasetId:N}");
            IReadOnlyList<PendingCommand> restored = await _gateway.ReadPendingAsync(cancellationToken).ConfigureAwait(false);
            lock (_state)
            {
                foreach (PendingCommand p in restored)
                    _pending[p.Identity.RequestId] = new PendingItem(p.Identity.RequestId, p.Identity.Command.Action, p.Identity.Command.SeatNo,
                        p.SavedUtc, PendingPhase.Unknown, "Khôi phục từ pending store sau khi khởi động lại");
            }
            Log(restored.Count == 0 ? "INFO" : "WARN", $"Khôi phục {restored.Count} lệnh pending chưa có kết quả.");
            Raise();
            await ResolveCoreAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Raise(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RefreshCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); Raise(); }
    }

    /// <summary>Ask the cluster for the FINAL result of every pending command with the SAME RequestId (GET_RESULT, then retry on NOT_FOUND).</summary>
    public async Task ResolvePendingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ResolveCoreAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); Raise(); }
    }

    /// <summary>Returns null when a local UX precondition blocks the command (the server stays authoritative).</summary>
    public async Task<CommandOutcome?> SubmitAsync(BookingAction action, int seatNo, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SeatActions allowed = GetActions(seatNo);
            bool ok = action switch { BookingAction.Hold => allowed.CanHold, BookingAction.Confirm => allowed.CanConfirm, _ => allowed.CanCancel };
            if (!ok)
            {
                Log("WARN", $"Bỏ qua {action} ghế {seatNo}: trạng thái hiện tại không cho phép.");
                return null;
            }
            Guid? reservationId = action == BookingAction.Hold ? null : BuildCells().First(c => c.SeatNo == seatNo).ReservationId;
            CommandData command = new(action, 1, seatNo, reservationId);
            Guid requestId = Guid.NewGuid();
            CommandIdentity identity = new(_datasetId, requestId, _ownerId,
                CanonicalProtocol.ComputeCommandHashString(_datasetId, _ownerId, action, 1, seatNo, reservationId), command);
            byte[] canonical = ReadModelCodec.WriteCanonicalCommand(command);

            PendingItem item = new(requestId, action, seatNo, DateTime.UtcNow, PendingPhase.Sending, "Đang gửi, chờ FINAL");
            lock (_state) { _pending[requestId] = item; _sent++; }
            Log("SEND", $"{action} ghế {seatNo} req={item.ShortId} hash={identity.CommandHash[..8]}");
            Raise();

            Stopwatch watch = Stopwatch.StartNew();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            CommandOutcome outcome = await _gateway.ExecuteAsync(identity, canonical, timeout.Token).ConfigureAwait(false);
            watch.Stop();
            lock (_state) _latenciesMs.Add(watch.Elapsed.TotalMilliseconds);

            await ApplyOutcomeAsync(item, outcome, resolvedLater: false, cancellationToken).ConfigureAwait(false);
            if (outcome.Kind == CommandOutcomeKind.Final) await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }
        finally { _gate.Release(); Raise(); }
    }

    /// <summary>Writes metrics.json next to the trace log and returns its path (K6 "ghi trace và số liệu").</summary>
    public string? ExportEvidence()
    {
        if (_evidenceDirectory is null) return null;
        ClientMetrics m = Metrics;
        string path = Path.Combine(_evidenceDirectory, $"metrics-c{_clientId}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            clientId = _clientId, ownerId = _ownerId, datasetId = _datasetId, exportedUtc = DateTime.UtcNow, metrics = m,
            pending = Pending.Select(p => new { p.RequestId, action = p.Action.ToString(), p.SeatNo, phase = p.Phase.ToString() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        Log("INFO", $"Đã xuất số liệu: {path}");
        return path;
    }

    private async Task ResolveCoreAsync(CancellationToken cancellationToken)
    {
        PendingItem[] items;
        lock (_state) items = _pending.Values.Where(p => p.Phase == PendingPhase.Unknown).OrderBy(p => p.SavedUtc).ToArray();
        foreach (PendingItem item in items)
        {
            Log("SEND", $"GET_RESULT req={item.ShortId} ({item.Action} ghế {item.SeatNo})");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            CommandOutcome outcome = await _gateway.ResolveAsync(item.RequestId, timeout.Token).ConfigureAwait(false);
            await ApplyOutcomeAsync(item, outcome, resolvedLater: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task ApplyOutcomeAsync(PendingItem item, CommandOutcome outcome, bool resolvedLater, CancellationToken cancellationToken)
    {
        switch (outcome.Kind)
        {
            case CommandOutcomeKind.Final:
                BusinessResult result = outcome.Result!;
                lock (_state)
                {
                    _pending.Remove(item.RequestId);
                    _final++;
                    if (resolvedLater) _resolvedLater++;
                    _last = new LastResult(item.RequestId, result.SeatNo, result.Code, ResultMessages.Describe(result.Code), ResultMessages.IsSuccess(result.Code), DateTime.UtcNow);
                }
                Log("FINAL", $"req={item.ShortId} {item.Action} ghế {result.SeatNo} -> {result.Code}{(resolvedLater ? " (xác định sau UNKNOWN)" : "")}");
                break;
            case CommandOutcomeKind.Rejected:
                lock (_state) { _pending[item.RequestId] = item with { Phase = PendingPhase.Rejected, Detail = outcome.Detail ?? "Bị từ chối" }; if (!resolvedLater) _rejected++; }
                Log("ERROR", $"req={item.ShortId} bị từ chối: {outcome.Detail}");
                break;
            default:
                lock (_state) { _pending[item.RequestId] = item with { Phase = PendingPhase.Unknown, Detail = outcome.Detail ?? "Chưa xác định" }; if (!resolvedLater) _unresolved++; }
                Log("WARN", $"req={item.ShortId} UNKNOWN: {outcome.Detail} (giữ pending, không tạo RequestId mới)");
                break;
        }
        Raise();
        return Task.CompletedTask;
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            StableSnapshot seats = await _gateway.ReadSeatsAsync(cancellationToken).ConfigureAwait(false);
            ReservationsSnapshot mine = await _gateway.ReadMyReservationsAsync(cancellationToken).ConfigureAwait(false);
            if (seats.DatasetId != _datasetId) throw new InvalidDataException("Snapshot DatasetId không khớp phiên.");
            lock (_state)
            {
                _board = seats;
                _reservations = mine.Reservations.Where(r => StringComparer.Ordinal.Equals(r.OwnerId, _ownerId)).ToArray();
                _boardReadUtc = DateTime.UtcNow;
                _stale = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            bool first;
            lock (_state) { first = !_stale; _stale = true; }
            if (first) Log("WARN", $"Không đọc được snapshot ({exception.GetType().Name}); sơ đồ đang là dữ liệu cũ.");
        }
        Raise();
    }

    private void Log(string level, string message)
    {
        TraceEntry entry = new(DateTime.UtcNow, level, message);
        lock (_state)
        {
            _trace.Add(entry);
            if (_trace.Count > TraceCapacity) _trace.RemoveAt(0);
            try { _traceFile?.WriteLine(entry.ToString()); } catch (IOException) { }
        }
    }

    private void Raise()
    {
        if (!_disposed) Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _traceFile?.Dispose();
        await _gateway.DisposeAsync().ConfigureAwait(false);
    }
}
