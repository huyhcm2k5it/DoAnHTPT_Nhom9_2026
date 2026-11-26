using De6.Client.Simulation;
using De6.Contracts;

namespace De6.Client.WinForms;

internal static class Program
{
    private const string Usage = "Usage: De6.Client.WinForms --config <path> [--check] [--demo]";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "--config")
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        bool check = false, demo = false;
        foreach (string flag in args.Skip(2))
        {
            if (flag == "--check") check = true;
            else if (flag == "--demo") demo = true;
            else { Console.Error.WriteLine(Usage); return 2; }
        }
        ClientOptions options;
        try { options = ConfigurationFile.ReadClient(args[1]); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Configuration error: {exception.Message}");
            return 2;
        }
        if (check)
        {
            Console.WriteLine($"C{options.ClientId} Owner={options.OwnerId}: configuration valid. UI K6 ready; use --demo for the simulated cluster (real wire protocol/TLS pins are not wired in this snapshot).");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        if (!demo)
        {
            MessageBox.Show(
                "Chưa nối được cụm thật: Server.Host chưa lắng nghe và chưa có triển khai IClientLoginProtocol/IClientNodeWireProtocol cùng TLS pin.\n\nChạy với --demo để dùng cụm giả lập (FAKE).",
                $"Quầy C{options.ClientId}", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 3;
        }
        return RunDemo(options);
    }

    private static int RunDemo(ClientOptions options)
    {
        using LoginForm login = new(options.ClientId, options.OwnerId, demo: true,
            (user, password) => DemoAuthenticator.Login(user, password, options.DatasetId));
        if (login.ShowDialog() != DialogResult.OK || login.Session is not { } session) return 0;
        if (!StringComparer.Ordinal.Equals(session.OwnerId, options.OwnerId))
        {
            MessageBox.Show($"Tài khoản {session.OwnerId} không thuộc quầy C{options.ClientId} (owner {options.OwnerId}).", "Đăng nhập", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 4;
        }

        string repositoryRoot = RepositoryRoot.Find();
        string clientDirectory = Path.Combine(repositoryRoot, options.DataDirectory);
        FileClientPendingStore store;
        try { store = new FileClientPendingStore(clientDirectory, options.DatasetId, session.OwnerId); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException)
        {
            MessageBox.Show($"Không mở được pending store của quầy C{options.ClientId}: {exception.Message}\nCó thể quầy này đang chạy ở tiến trình khác.", "Pending store", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 5;
        }

        SimulatedFaults faults = new();
        SimulatedCluster cluster = new(Path.Combine(repositoryRoot, "data", "demo"), options.DatasetId);
        ClientRouteManager routes = new(options, new SimulatedNodeConnectionFactory(cluster, session.OwnerId, faults));
        SdkBookingGateway gateway = new(options, session.OwnerId, store, routes);
        BookingController controller = new(gateway, session.OwnerId, options.DatasetId, options.ClientId, Path.Combine(clientDirectory, "evidence"));
        try
        {
            Application.Run(new MainForm(controller, faults, demo: true));
        }
        finally
        {
            Task.Run(() => controller.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        return 0;
    }
}
