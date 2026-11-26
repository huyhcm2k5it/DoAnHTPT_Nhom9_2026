using System.Drawing.Drawing2D;

namespace De6.Client.WinForms;

/// <summary>White + sky-blue palette. Seat state is always shown by label and pattern as well as colour (V12 section 17).</summary>
internal static class Theme
{
    public static readonly Color White = Color.White;
    public static readonly Color Sky = Color.FromArgb(0x3A, 0xA8, 0xF0);
    public static readonly Color SkyDeep = Color.FromArgb(0x1B, 0x7F, 0xD1);
    public static readonly Color Navy = Color.FromArgb(0x1F, 0x4E, 0x79);
    public static readonly Color SkyPale = Color.FromArgb(0xEA, 0xF5, 0xFD);
    public static readonly Color SkyBorder = Color.FromArgb(0xBF, 0xE0, 0xF7);
    public static readonly Color Ink = Color.FromArgb(0x17, 0x32, 0x4D);
    public static readonly Color Muted = Color.FromArgb(0x5B, 0x7A, 0x94);
    public static readonly Color Warn = Color.FromArgb(0xD9, 0x82, 0x00);
    public static readonly Color Danger = Color.FromArgb(0xD6, 0x45, 0x45);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 11f);
    public static readonly Font Mono = new("Consolas", 9f);

    public static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.Font = Bold;
        button.Height = 36;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = Sky;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? SkyDeep : SkyPale;
        button.BackColor = primary ? Sky : White;
        button.ForeColor = primary ? White : SkyDeep;
    }

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        GraphicsPath path = new();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
