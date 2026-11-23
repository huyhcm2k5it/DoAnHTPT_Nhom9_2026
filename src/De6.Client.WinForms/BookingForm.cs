using System.Text.Json;
using De6.Client.Sdk;
using De6.Contracts;

namespace De6.Client.WinForms;

internal sealed class BookingForm : Form
{
    private readonly ClientOptions _options;
    private readonly TextBox _username = new() { Width = 100 };
    private readonly TextBox _password = new() { Width = 130, UseSystemPasswordChar = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _pending = new() { AutoSize = true };
    private readonly ComboBox _seat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 65 };
    private readonly Button _login = new() { Text = "Đăng nhập", AutoSize = true };
    private readonly FlowLayoutPanel _commands = new() { AutoSize = true, Dock = DockStyle.Fill, Enabled = false };
    private readonly Label[] _seats = new Label[10];
    private readonly Dictionary<int, SeatRow> _snapshot = [];
    private ClientRouteManager? _router;
    private FileClientPendingStore? _store;
    private ClientCommandExecutor? _executor;
    private CancellationTokenSource? _operation;
    private Task? _active;
    private bool _closing;

    public BookingForm(ClientOptions options)
    {
        _options = options;
        Text = $"Đặt vé tàu · Quầy C{options.ClientId} ({options.OwnerId})";
        MinimumSize = new Size(720, 380);
        ClientSize = new Size(780, 410);
        StartPosition = FormStartPosition.CenterScreen;
        _username.Text = options.OwnerId;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var loginPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        loginPanel.Controls.AddRange([new Label { Text = "Tài khoản", AutoSize = true }, _username,
            new Label { Text = "Mật khẩu", AutoSize = true }, _password, _login]);
        layout.Controls.Add(loginPanel, 0, 0);

        var seats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2 };
        for (var i = 0; i < 5; i++) seats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (var i = 0; i < 2; i++) seats.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (var i = 0; i < _seats.Length; i++)
        {
            _seats[i] = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle, Text = $"Ghế {i + 1}\nChưa tải" };
            seats.Controls.Add(_seats[i], i % 5, i / 5);
            _seat.Items.Add(i + 1);
        }
        _seat.SelectedIndex = 0;
        layout.Controls.Add(seats, 0, 1);
        _commands.Controls.AddRange([new Label { Text = "Ghế", AutoSize = true }, _seat]);
        AddButton("Giữ ghế", token => SubmitAsync(BookingAction.Hold, token));
        AddButton("Mua vé", token => SubmitAsync(BookingAction.Confirm, token));
        AddButton("Hủy giữ", token => SubmitAsync(BookingAction.Cancel, token));
        AddButton("Tải lại", RefreshAsync);
        AddButton("Tra lệnh đang chờ", ResolveAsync);
        layout.Controls.Add(_commands, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(_pending, 0, 4);
        Controls.Add(layout);
        _status.Text = "Đăng nhập để tải trạng thái ghế.";
        _login.Click += (_, _) => Start(LoginAsync);
        FormClosing += CloseAsync;
    }

    private void AddButton(string caption, Func<CancellationToken, Task> action)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += (_, _) => Start(action);
        _commands.Controls.Add(button);
    }

    private void Start(Func<CancellationToken, Task> action)
    {
        if (_closing || _operation is not null) return;
        _operation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _active = RunAsync(action, _operation);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationTokenSource operation)
    {
        _commands.Enabled = _login.Enabled = false;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { _status.Text = "Đã hết thời gian chờ. Kiểm tra lệnh đang chờ."; }
        catch (Exception ex) { _status.Text = $"Thao tác chưa hoàn tất. {ex.Message}"; }
        finally
        {
            try
            {
                if (_executor is not null)
                    _pending.Text = $"Lệnh đang chờ: {(await _executor.RestorePendingAsync()).Count}";
            }
            catch (IOException) { _pending.Text = "Không đọc được các lệnh đang chờ."; }
            _login.Enabled = !_closing && _router is null;
            _commands.Enabled = !_closing && _executor is not null;
            _operation = null;
            operation.Dispose();
        }
    }

    private async Task LoginAsync(CancellationToken token)
    {
        var path = Environment.GetEnvironmentVariable("DE6_CLIENT_TLS_PINS");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Thiếu đường dẫn DE6_CLIENT_TLS_PINS.");
        var pins = JsonSerializer.Deserialize<Dictionary<int, string>>(await File.ReadAllBytesAsync(path, token));
        if (pins is null || !_options.Servers.All(server => pins.ContainsKey(server.NodeId)))
            throw new InvalidDataException("Cần cấu hình pin chứng thư của cả ba server.");
        var factory = new TlsClientNodeConnectionFactory(endpoint => pins[endpoint.NodeId],
            new ClientLoginRequest(_username.Text, _password.Text, _options.DatasetId), _options.OwnerId);
        _password.Clear();
        var router = new ClientRouteManager(_options, factory);
        FileClientPendingStore? store = null;
        try
        {
            await router.RefreshPrimaryAsync(token);
            store = new FileClientPendingStore(_options.DataDirectory, _options.DatasetId, _options.OwnerId);
            _executor = new ClientCommandExecutor(store, router, _options.DatasetId, _options.OwnerId);
            _store = store;
            _router = router;
        }
        catch
        {
            if (store is not null) await store.DisposeAsync();
            await router.DisposeAsync();
            throw;
        }
        await ResolveAsync(token);
    }

    private async Task ResolveAsync(CancellationToken token)
    {
        var resolved = await _executor!.ResolveAllPendingAsync(token);
        await RefreshAsync(token);
        if (resolved.Count > 0)
            _status.Text += $" · Đã tra {resolved.Count}, có {resolved.Count(r => r.IsFinal)} kết quả FINAL.";
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        using var document = JsonDocument.Parse("{\"type\":\"GET_SEATS\"}");
        var response = await _router!.SendAsync(new ClientRequest(null, document.RootElement), token);
        if (response.Payload.GetProperty("status").GetString() != "FINAL")
        {
            _status.Text = "Cụm chưa sẵn sàng. Hiển thị trạng thái đã tải gần nhất.";
            return;
        }
        var rows = response.Payload.GetProperty("seats").EnumerateArray().Select(row => new SeatRow(
            row.GetProperty("seatNo").GetInt32(), row.GetProperty("status").GetString()!,
            row.GetProperty("ownerId").GetString(),
            row.GetProperty("reservationId").TryGetGuidIfString())).ToArray();
        if (!rows.Select(row => row.SeatNo).Order().SequenceEqual(Enumerable.Range(1, 10)))
            throw new InvalidDataException("Server phải trả đủ 10 ghế.");
        _snapshot.Clear();
        foreach (var row in rows)
        {
            _snapshot.Add(row.SeatNo, row);
            _seats[row.SeatNo - 1].Text = $"Ghế {row.SeatNo}\n{row.Status}" +
                (row.OwnerId == _options.OwnerId ? "\nQuầy của bạn" : "");
        }
        _status.Text = $"S{response.SenderId} · Mốc {response.Payload.GetProperty("appliedIndex").GetInt64()}";
    }

    private async Task SubmitAsync(BookingAction action, CancellationToken token)
    {
        var seat = (int)_seat.SelectedItem!;
        if (!_snapshot.TryGetValue(seat, out var row) ||
            (action == BookingAction.Hold ? row.Status != "AVAILABLE" :
                row.Status != "HELD" || row.OwnerId != _options.OwnerId || row.ReservationId is null))
        {
            _status.Text = "Ghế không phù hợp với thao tác. Tải lại trạng thái và thử lại.";
            return;
        }
        var command = new CommandData(action, 1, seat, action == BookingAction.Hold ? null : row.ReservationId);
        var identity = new CommandIdentity(_options.DatasetId, Guid.NewGuid(), _options.OwnerId,
            BookingCanonical.CommandHash(_options.DatasetId, _options.OwnerId, command), command);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = action.ToString().ToUpperInvariant(), tripId = command.TripId,
            seatNo = command.SeatNo, reservationId = command.ReservationId
        });
        var response = await _executor!.ExecuteAsync(identity, payload, _ => null, token);
        var status = response.Payload.GetProperty("status").GetString();
        if (status == "FINAL")
        {
            var result = response.Payload.GetProperty("result");
            var code = result.GetProperty("code").GetString();
            await RefreshAsync(token);
            _status.Text = result.GetProperty("ticketId").TryGetGuidIfString() is { } ticket
                ? $"Ghế {seat} · {code} · Vé {ticket:D}" : $"Ghế {seat} · {code}";
        }
        else _status.Text = $"Ghế {seat} · {status} · Kiểm tra lệnh đang chờ.";
    }

    private async void CloseAsync(object? sender, FormClosingEventArgs args)
    {
        if (_closing) return;
        args.Cancel = true;
        _closing = true;
        _operation?.Cancel();
        try
        {
            if (_active is not null) await _active;
            if (_router is not null) await _router.DisposeAsync();
            if (_store is not null) await _store.DisposeAsync();
        }
        finally { BeginInvoke(Close); }
    }

    private sealed record SeatRow(int SeatNo, string Status, string? OwnerId, Guid? ReservationId);
}

internal static class BookingJson
{
    internal static Guid? TryGetGuidIfString(this JsonElement element) =>
        element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var id) ? id : null;
}
