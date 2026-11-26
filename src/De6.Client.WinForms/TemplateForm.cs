using De6.Contracts;

namespace De6.Client.WinForms;

internal sealed class TemplateForm : Form
{
    public TemplateForm(ClientOptions options)
    {
        Text = $"De 6 - C{options.ClientId} / Owner {options.OwnerId} - Template";
        ClientSize = new Size(760, 360);
        MinimumSize = new Size(720, 390);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "TEMPLATE: connection, authentication and pending store NOT IMPLEMENTED.\nSeats below have UNKNOWN state; no booking data has been loaded.", AutoSize = false }, 0, 0);
        var seats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2 };
        for (var column = 0; column < 5; column++) seats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (var row = 0; row < 2; row++) seats.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (var seat = 1; seat <= 10; seat++)
            seats.Controls.Add(new Button { Text = $"Seat {seat}\nUNKNOWN", Enabled = false, Dock = DockStyle.Fill, Margin = new Padding(5) }, (seat - 1) % 5, (seat - 1) / 5);
        layout.Controls.Add(seats, 0, 1);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = $"Owner: {options.OwnerId} | Dataset: {options.DatasetId:D}\nHOLD / CONFIRM / CANCEL disabled until the V12 service is implemented." }, 0, 2);
        Controls.Add(layout);
    }
}
