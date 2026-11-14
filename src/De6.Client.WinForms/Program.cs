using De6.Contracts;

namespace De6.Client.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || args[0] != "--config" || (args.Length == 3 && args[2] != "--check"))
        {
            Console.Error.WriteLine("Usage: De6.Client.WinForms --config <path> [--check]");
            return 2;
        }
        ClientOptions options;
        try { options = ConfigurationFile.ReadClient(args[1]); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Configuration error: {exception.Message}");
            return 2;
        }
        if (args.Contains("--check"))
        {
            Console.WriteLine($"C{options.ClientId} Owner={options.OwnerId}: configuration valid; connection/pending NOT IMPLEMENTED.");
            return 0;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new TemplateForm(options));
        return 0;
    }
}
