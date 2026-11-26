namespace De6.Client.WinForms;

/// <summary>Counter login. The OwnerId comes from the resulting session, never from anything typed into the booking UI.</summary>
internal sealed class LoginForm : Form
{
    private readonly TextBox _user = new();
    private readonly TextBox _password = new();
    private readonly Label _error = new();
    private readonly Func<string, string, ClientSession> _authenticate;

    internal ClientSession? Session { get; private set; }

    public LoginForm(int clientId, string expectedOwner, bool demo, Func<string, string, ClientSession> authenticate)
    {
        _authenticate = authenticate;
        Text = $"Đăng nhập quầy C{clientId}";
        ClientSize = new Size(380, demo ? 330 : 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.White;
        Font = Theme.Body;

        Panel header = new() { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Sky };
        header.Controls.Add(new Label { Text = $"Quầy C{clientId}", ForeColor = Theme.White, Font = Theme.Title, AutoSize = true, Location = new Point(20, 12) });
        header.Controls.Add(new Label { Text = "Hệ thống bán vé tàu · Chuyến 1 · 10 ghế", ForeColor = Theme.White, AutoSize = true, Location = new Point(22, 48) });

        Label userLabel = new() { Text = "Tài khoản quầy", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 98) };
        _user.Location = new Point(24, 120); _user.Width = 332; _user.Text = expectedOwner; _user.Font = Theme.Body;
        Label passLabel = new() { Text = "Mật khẩu", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 156) };
        _password.Location = new Point(24, 178); _password.Width = 332; _password.UseSystemPasswordChar = true; _password.Font = Theme.Body;
        _error.ForeColor = Theme.Danger; _error.AutoSize = false; _error.Location = new Point(24, 212); _error.Size = new Size(332, 20);

        Button login = new() { Text = "Đăng nhập", Location = new Point(24, 238), Width = 332 };
        Theme.StyleButton(login, primary: true);
        login.Click += (_, _) => TryLogin();
        AcceptButton = login;

        Controls.AddRange([header, userLabel, _user, passLabel, _password, _error, login]);
        if (demo)
            Controls.Add(new Label { Text = "CHẾ ĐỘ DEMO (giả lập cụm): mật khẩu là \"demo\".", ForeColor = Theme.Warn, AutoSize = false, Location = new Point(24, 288), Size = new Size(332, 30) });
    }

    private void TryLogin()
    {
        try
        {
            Session = _authenticate(_user.Text, _password.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException)
        {
            _error.Text = exception.Message;
            _password.Clear();
            _password.Focus();
        }
    }
}
