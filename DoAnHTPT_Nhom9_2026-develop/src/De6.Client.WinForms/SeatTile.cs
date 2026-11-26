using System.Drawing.Drawing2D;
using De6.Contracts;

namespace De6.Client.WinForms;

/// <summary>One seat. Confirmed status (label + colour + pattern) and the pending/UNKNOWN badge are drawn separately.</summary>
internal sealed class SeatTile : Control
{
    private SeatCell? _cell;
    private bool _selected;
    private bool _hover;

    public SeatTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Margin = new Padding(6);
        TabStop = true;
    }

    internal int SeatNo => _cell?.SeatNo ?? 0;

    internal void Bind(SeatCell cell, bool selected)
    {
        _cell = cell;
        _selected = selected;
        AccessibleName = $"Ghế {cell.SeatNo}: {StatusLabel(cell)}{(cell.Pending is null ? "" : ", " + PendingLabel(cell.Pending))}";
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.White);
        if (_cell is null) return;

        Rectangle bounds = new(2, 2, Width - 5, Height - 5);
        Color fill, border, text;
        bool hatch = false;
        switch (_cell.Status)
        {
            case SeatStatus.Available: fill = Theme.White; border = Theme.Sky; text = Theme.SkyDeep; break;
            case SeatStatus.Held when _cell.HeldByMe: fill = Theme.Sky; border = Theme.SkyDeep; text = Theme.White; break;
            case SeatStatus.Held: fill = Theme.SkyPale; border = Theme.SkyBorder; text = Theme.Navy; hatch = true; break;
            case SeatStatus.Sold: fill = Theme.Navy; border = Theme.Navy; text = Theme.White; break;
            default: fill = Theme.SkyPale; border = Theme.SkyBorder; text = Theme.Muted; break;
        }
        if (_hover && _cell.Status == SeatStatus.Available) fill = Theme.SkyPale;

        using (GraphicsPath path = Theme.RoundedRectangle(bounds, 10))
        {
            if (hatch)
            {
                using HatchBrush brush = new(HatchStyle.LightUpwardDiagonal, Theme.SkyBorder, Theme.SkyPale);
                g.FillPath(brush, path);
            }
            else
            {
                using SolidBrush brush = new(fill);
                g.FillPath(brush, path);
            }
            using Pen pen = new(_selected ? Theme.Ink : border, _selected ? 3f : 2f);
            g.DrawPath(pen, path);
        }

        using SolidBrush textBrush = new(text);
        using StringFormat center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using Font number = new("Segoe UI Semibold", Math.Max(14f, Height / 4.2f));
        g.DrawString(_cell.SeatNo.ToString(), number, textBrush, new RectangleF(bounds.X, bounds.Y + 6, bounds.Width, bounds.Height * 0.55f), center);
        g.DrawString(StatusLabel(_cell), Theme.Bold, textBrush, new RectangleF(bounds.X, bounds.Y + bounds.Height * 0.58f, bounds.Width, bounds.Height * 0.3f), center);

        if (_cell.Pending is { } pending)
        {
            string badge = PendingLabel(pending);
            SizeF size = g.MeasureString(badge, Theme.Mono);
            Rectangle chip = new(bounds.Right - (int)size.Width - 12, bounds.Y + 6, (int)size.Width + 8, (int)size.Height + 2);
            using GraphicsPath chipPath = Theme.RoundedRectangle(chip, 6);
            using SolidBrush chipFill = new(pending.Phase == PendingPhase.Rejected ? Theme.Danger : Theme.Warn);
            g.FillPath(chipFill, chipPath);
            g.DrawString(badge, Theme.Mono, Brushes.White, chip.X + 4, chip.Y + 1);
        }
    }

    internal static string StatusLabel(SeatCell cell) => cell.Status switch
    {
        SeatStatus.Available => "AVAILABLE",
        SeatStatus.Held => cell.HeldByMe ? "HELD · của tôi" : "HELD",
        SeatStatus.Sold => "SOLD",
        _ => "CHƯA TẢI"
    };

    internal static string PendingLabel(PendingItem item) => item.Phase switch
    {
        PendingPhase.Sending => "ĐANG GỬI",
        PendingPhase.Rejected => "BỊ TỪ CHỐI",
        _ => "UNKNOWN"
    };
}
