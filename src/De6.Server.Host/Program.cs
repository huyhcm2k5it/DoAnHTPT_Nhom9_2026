using De6.Cluster.Control;
using De6.Contracts;

if (args.Length is < 2 or > 3 || args[0] != "--config" || (args.Length == 3 && args[2] != "--check"))
{
    Console.Error.WriteLine("Usage: De6.Server.Host --config <path> [--check]");
    return 2;
}
ServerOptions options;
try { options = ConfigurationFile.ReadServer(args[1]); }
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
{
    Console.Error.WriteLine($"Configuration error: {exception.Message}");
    return 2;
}
var state = new BootstrapState();
Console.WriteLine($"S{options.NodeId} DatasetId={options.DatasetId:D} Role={state.Role} ServingEnabled={state.ServingEnabled.ToString().ToLowerInvariant()}");
Console.WriteLine("TEMPLATE ONLY: configuration validated. SQL/TLS/TCP listeners, coordinator, election, replication and activation are NOT IMPLEMENTED.");
Console.WriteLine("No SQL health was checked. No booking service is listening. No FINAL responses can be produced.");
if (args.Contains("--check")) return 0;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
Console.WriteLine("Bootstrap waiting. Press Ctrl+C to stop.");
try { await Task.Delay(Timeout.Infinite, stop.Token); }
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
return 0;
