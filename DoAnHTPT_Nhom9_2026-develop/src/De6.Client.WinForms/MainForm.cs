using De6.Client.Simulation;
using De6.Contracts;

namespace De6.Client.WinForms;

internal sealed class MainForm : Form
{
    private readonly BookingController _controller;
    private readonly SimulatedFaults? _faults;
    private readonly SeatTile[] _tiles = new SeatTile[10];
    private readonly Label _connection = new();
    private readonly Label _boardInfo = new();
    private readonly Label _selectedLabel = new();
    private readonly Label _status = new();
    private readonly Label _metrics = new();
    private readonly Button _hold = new();
    private readonly Button _confirm = new();
    private readonly Button _cancel = new();
    private readonly Button _refresh = new();
    private readonly Button _resolve = new();
    private readonly Button _export = new();
    private readonly ListView _mine = new();
    private readonly ListView _pending = new();
    private readonly TextBox _trace = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private int _selectedSeat = 1;
    private int _renderedTrace = -1;

    public MainForm(BookingController controller, SimulatedFaults? faults, bool demo)
    {
        _controller = controller;
        _faults = faults;
        Text = $"Bán vé tàu · Quầy C{controller.ClientId} · Owner {controller.OwnerId}";
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.SkyPale;
        Font = Theme.Body;

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = demo ? 4 : 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        if (demo) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.Controls.Add(BuildHeader(demo), 0, 0);
        int row = 1;
        if (demo) root.Controls.Add(BuildDemoBar(), 0, row++);
        root.Controls.Add(BuildBody(), 0, row++);
        root.Controls.Add(BuildStatusBar(), 0, row);
        Controls.Add(root);

        _controller.Changed += OnControllerChanged;
        _timer.Tick += async (_, _) => await OnTimerAsync();
        Load += async (_, _) => await RunAsync(() => _controller.StartAsync());
        FormClosing += (_, _) => { _timer.Stop(); _controller.Changed -= OnControllerChanged; };
        Render();
        _timer.Start();
    }

    private Control BuildHeader(bool demo)
    {
        Panel header = new() { Dock = DockStyle.Fill, BackColor = Theme.Sky, Padding = new Padding(20, 10, 20, 8) };
        Label title = new() { Text = $"Quầy C{_controller.ClientId} · Owner {_controller.OwnerId}", ForeColor = Theme.White, Font = Theme.Title, AutoSize = true, Location = new Point(20, 8) };
        Label sub = new() { Text = "Bán vé tàu cao tốc · Chuyến 1 · 10 ghế", ForeColor = Theme.White, AutoSize = true, Location = new Point(22, 42) };
        _connection.ForeColor = Theme.White;
        _connection.AutoSize = true;
        _connection.Location = new Point(700, 12);
        header.Controls.AddRange([title, sub, _connection]);
        Label? badge = null;
        if (demo)
        {
            badge = new Label { Text = " DEMO · CỤM GIẢ LẬP ", ForeColor = Theme.SkyDeep, BackColor = Theme.White, AutoSize = true, Font = Theme.Bold, Location = new Point(880, 38) };
            header.Controls.Add(badge);
        }
        header.Resize += (_, _) =>
        {
            _connection.Left = header.Width - _connection.Width - 24;
            if (badge is not null) badge.Left = header.Width - badge.Width - 24;
        };
        return header;
    }

    private Control BuildDemoBar()
    {
        FlowLayoutPanel bar = new() { Dock = DockStyle.Fill, BackColor = Theme.White, Padding = new Padding(16, 6, 16, 4), WrapContents = false };
        bar.Controls.Add(new Label { Text = "Tiêm lỗi (FAKE):", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
        CheckBox down = new() { Text = "Mất mạng", AutoSize = true, ForeColor = Theme.Ink, Margin = new Padding(0, 6, 14, 0) };
        down.CheckedChanged += (_, _) => { if (_faults is not null) _faults.NetworkDown = down.Checked; };
        Button drop = new() { Text = "Rớt response lần sau", Width = 170 };
        Button cut = new() { Text = "Rớt response + cắt mạng", Width = 190 };
        Theme.StyleButton(drop, false); Theme.StyleButton(cut, false);
        drop.Height = 30; cut.Height = 30;
        drop.Click += (_, _) => { if (_faults is not null) _faults.DropNextResponse = true; SetStatus("Lệnh kế tiếp sẽ được cluster áp dụng nhưng response bị rớt.", false); };
        cut.Click += (_, _) => { if (_faults is not null) _faults.CutAfterNextApply = true; SetStatus("Lệnh kế tiếp: áp dụng, rớt response và cắt mạng. Bỏ chọn \"Mất mạng\" để tra lại kết quả.", false); };
        System.Windows.Forms.Timer sync = new() { Interval = 500 };
        sync.Tick += (_, _) => { if (_faults is not null && down.Checked != _faults.NetworkDown) down.Checked = _faults.NetworkDown; };
        sync.Start();
        bar.Controls.AddRange([down, drop, cut]);
        return bar;
    }

    private Control BuildBody()
    {
        TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16, 12, 16, 8), BackColor = Theme.SkyPale };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        body.Controls.Add(BuildSeatCard(), 0, 0);
        body.Controls.Add(BuildRightColumn(), 1, 0);
        Control traceCard = BuildTraceCard();
        body.Controls.Add(traceCard, 0, 1);
        body.SetColumnSpan(traceCard, 2);
        return body;
    }

    private static Panel Card(string title, Control content, Control? headerRight = null)
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.White, Padding = new Padding(14, 10, 14, 12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Font = Theme.CardTitle, ForeColor = Theme.SkyDeep, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom }, 0, 0);
        if (headerRight is not null) layout.Controls.Add(headerRight, 1, 0);
        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);
        layout.SetColumnSpan(content, 2);
        Panel frame = new() { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.SkyBorder, Margin = new Padding(0, 0, 10, 10) };
        frame.Controls.Add(layout);
        return frame;
    }

    private Control BuildSeatCard()
    {
        TableLayoutPanel grid = new() { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, BackColor = Theme.White };
        for (int c = 0; c < 5; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (int r = 0; r < 2; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int i = 0; i < 10; i++)
        {
            SeatTile tile = new() { Dock = DockStyle.Fill };
            int seat = i + 1;
            tile.Click += (_, _) => { _selectedSeat = seat; Render(); };
            _tiles[i] = tile;
            grid.Controls.Add(tile, i % 5, i / 5);
        }

        _boardInfo.AutoSize = true;
        _boardInfo.ForeColor = Theme.Muted;
        _selectedLabel.AutoSize = true;
        _selectedLabel.Font = Theme.Bold;
        _selectedLabel.ForeColor = Theme.Ink;
        _selectedLabel.Margin = new Padding(0, 10, 16, 0);
        _hold.Text = "Giữ ghế"; _confirm.Text = "Xác nhận mua"; _cancel.Text = "Hủy giữ"; _refresh.Text = "Làm mới";
        foreach (Button b in new[] { _hold, _confirm, _cancel, _refresh }) { b.Width = 130; b.Margin = new Padding(0, 4, 8, 0); }
        Theme.StyleButton(_hold, true); Theme.StyleButton(_confirm, true); Theme.StyleButton(_cancel, false); Theme.StyleButton(_refresh, false);
        _hold.Click += async (_, _) => await RunAsync(() => _controller.SubmitAsync(BookingAction.Hold, _selectedSeat));
        _confirm.Click += async (_, _) => await RunAsync(() => _controller.SubmitAsync(BookingAction.Confirm, _selectedSeat));
        _cancel.Click += async (_, _) => await RunAsync(() => _controller.SubmitAsync(BookingAction.Cancel, _selectedSeat));
        _refresh.Click += async (_, _) => await RunAsync(() => _controller.RefreshAsync());

        FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.White };
        actions.Controls.AddRange([_selectedLabel, _hold, _confirm, _cancel, _refresh]);
        Label legend = new()
        {
            Text = "AVAILABLE: viền xanh · HELD của tôi: nền xanh · HELD khác: nền sọc · SOLD: xanh đậm · Nhãn cam: lệnh đang gửi/UNKNOWN (chưa xác nhận)",
            ForeColor = Theme.Muted, AutoSize = false, Dock = DockStyle.Fill
        };

        TableLayoutPanel content = new() { ColumnCount = 1, RowCount = 3, BackColor = Theme.White };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        content.Controls.Add(grid, 0, 0);
        content.Controls.Add(actions, 0, 1);
        content.Controls.Add(legend, 0, 2);
        return Card("Sơ đồ 10 ghế", content, _boardInfo);
    }

    private Control BuildRightColumn()
    {
        ConfigureList(_mine, ("Ghế", 50), ("Reservation", 110), ("Trạng thái", 100));
        ConfigureList(_pending, ("Lệnh", 80), ("Ghế", 40), ("RequestId", 80), ("Trạng thái", 100), ("Chi tiết", 260));
        _resolve.Text = "Tra kết quả (GET_RESULT)";
        _resolve.Width = 190;
        Theme.StyleButton(_resolve, false);
        _resolve.Height = 28;
        _resolve.Click += async (_, _) => await RunAsync(() => _controller.ResolvePendingAsync());

        TableLayoutPanel column = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.SkyPale };
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        column.Controls.Add(Card("Giữ ghế của quầy", _mine), 0, 0);
        column.Controls.Add(Card("Lệnh chờ kết quả (pending / UNKNOWN)", _pending, _resolve), 0, 1);
        return column;
    }

    private Control BuildTraceCard()
    {
        _trace.Multiline = true; _trace.ReadOnly = true; _trace.ScrollBars = ScrollBars.Vertical;
        _trace.Font = Theme.Mono; _trace.BackColor = Theme.SkyPale; _trace.ForeColor = Theme.Ink; _trace.BorderStyle = BorderStyle.None;
        _export.Text = "Xuất số liệu";
        _export.Width = 120;
        Theme.StyleButton(_export, false);
        _export.Height = 28;
        _export.Click += (_, _) =>
        {
            string? path = _controller.ExportEvidence();
            SetStatus(path is null ? "Chưa cấu hình thư mục evidence." : $"Đã xuất số liệu: {path}", false);
        };
        Panel card = Card("Trace (gửi · FINAL · UNKNOWN)", _trace, _export);
        card.Margin = new Padding(0, 0, 10, 0);
        return card;
    }

    private Control BuildStatusBar()
    {
        TableLayoutPanel bar = new() { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.White, Padding = new Padding(16, 0, 16, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.ForeColor = Theme.Ink;
        _metrics.Dock = DockStyle.Fill; _metrics.TextAlign = ContentAlignment.MiddleRight; _metrics.ForeColor = Theme.Muted;
        bar.Controls.Add(_status, 0, 0);
        bar.Controls.Add(_metrics, 1, 0);
        return bar;
    }

    private static void ConfigureList(ListView list, params (string Name, int Width)[] columns)
    {
        list.View = View.Details; list.FullRowSelect = true; list.HideSelection = false; list.BorderStyle = BorderStyle.None;
        list.BackColor = Theme.White; list.ForeColor = Theme.Ink; list.Font = Theme.Body; list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        foreach ((string name, int width) in columns) list.Columns.Add(name, width);
    }

    private void OnControllerChanged()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Render));
        }
        catch (InvalidOperationException) { }
    }

    private async Task OnTimerAsync()
    {
        if (_controller.Busy || IsDisposed) return;
        bool unknown = _controller.Pending.Any(p => p.Phase == PendingPhase.Unknown);
        await RunAsync(() => unknown ? _controller.ResolvePendingAsync() : _controller.RefreshAsync(), quiet: true);
    }

    private async Task RunAsync(Func<Task> action, bool quiet = false)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!IsDisposed)
        {
            SetStatus($"Lỗi: {exception.Message}", true);
            if (!quiet) MessageBox.Show(this, exception.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (!IsDisposed) Render();
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.ForeColor = error ? Theme.Danger : Theme.Ink;
    }

    private void Render()
    {
        if (IsDisposed) return;
        ClientNodeStatus? node = _controller.NodeStatus;
        _connection.Text = node is null
            ? "Chưa kết nối primary"
            : $"Primary S{node.NodeId} · epoch {node.Epoch} · stable {node.StableIndex}";

        IReadOnlyList<SeatCell> cells = _controller.BuildCells();
        for (int i = 0; i < 10; i++) _tiles[i].Bind(cells[i], cells[i].SeatNo == _selectedSeat);

        if (_controller.Board is { } board && _controller.BoardReadUtc is { } read)
            _boardInfo.Text = _controller.BoardIsStale
                ? $"DỮ LIỆU CŨ (lần đọc cuối {read.ToLocalTime():HH:mm:ss}) · không dùng để xác nhận"
                : $"Snapshot Stable · applied {board.AppliedIndex} · {read.ToLocalTime():HH:mm:ss}";
        else
            _boardInfo.Text = _controller.BoardIsStale ? "Chưa đọc được sơ đồ" : "Đang tải sơ đồ…";
        _boardInfo.ForeColor = _controller.BoardIsStale ? Theme.Warn : Theme.Muted;

        SeatActions actions = _controller.GetActions(_selectedSeat);
        bool idle = !_controller.Busy;
        _selectedLabel.Text = $"Ghế {_selectedSeat}";
        _hold.Enabled = idle && actions.CanHold;
        _confirm.Enabled = idle && actions.CanConfirm;
        _cancel.Enabled = idle && actions.CanCancel;
        _refresh.Enabled = idle;

        _mine.BeginUpdate();
        _mine.Items.Clear();
        foreach (ReservationData r in _controller.MyReservations.OrderByDescending(x => x.UpdatedIndex))
            _mine.Items.Add(new ListViewItem(new string[] { r.SeatNo.ToString(), r.ReservationId.ToString("N")[..8], r.Status.ToString().ToUpperInvariant() }));
        _mine.EndUpdate();

        IReadOnlyList<PendingItem> pending = _controller.Pending;
        _pending.BeginUpdate();
        _pending.Items.Clear();
        foreach (PendingItem p in pending)
        {
            ListViewItem item = new(new string[] { p.Action.ToString().ToUpperInvariant(), p.SeatNo.ToString(), p.ShortId, SeatTile.PendingLabel(p), p.Detail });
            item.ForeColor = p.Phase == PendingPhase.Rejected ? Theme.Danger : Theme.Warn;
            _pending.Items.Add(item);
        }
        _pending.EndUpdate();
        _resolve.Enabled = idle && pending.Any(p => p.Phase == PendingPhase.Unknown);

        if (_controller.Last is { } last)
            SetStatus($"FINAL · ghế {last.SeatNo}: {last.Text} ({last.Code})", !last.Success);
        else if (pending.Count > 0)
            SetStatus($"{pending.Count} lệnh chưa có kết quả FINAL — chưa được tính là thành công.", false);

        ClientMetrics m = _controller.Metrics;
        _metrics.Text = $"Gửi {m.Sent} · FINAL {m.Final} · UNKNOWN {m.Unresolved} · xác định sau {m.ResolvedLater} · TB {m.AverageMs:F0} ms · p95 {m.P95Ms:F0} ms";

        IReadOnlyList<TraceEntry> trace = _controller.Trace;
        if (trace.Count != _renderedTrace)
        {
            _renderedTrace = trace.Count;
            _trace.Text = string.Join(Environment.NewLine, trace.TakeLast(200).Select(t => t.ToString()));
            _trace.SelectionStart = _trace.TextLength;
            _trace.ScrollToCaret();
        }
    }
}
