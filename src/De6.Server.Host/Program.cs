using De6.Cluster.Control;
using De6.Contracts;
using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;

if (args.Length is < 2 or > 3 || args[0] != "--config" || (args.Length == 3 && args[2] is not ("--check" or "--check-sql")))
{
    Console.Error.WriteLine("Usage: De6.Server.Host --config <path> [--check|--check-sql]");
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
Console.WriteLine("No booking service is listening. No FINAL responses can be produced. Activation/coordinator/TLS/TCP/election/replication remain unimplemented.");
if (args.Contains("--check"))
{
    Console.WriteLine("Configuration validated only; no SQL access was attempted.");
    return 0;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
if(options.Sql is not null)
{
    try
    {
        var health=await new SqlStorageHealth().ProbeAsync(SqlPersistenceComposition.CreateStoreOptions(options),stop.Token);
        Console.WriteLine($"SQL local probe passed: SchemaVersion={health.SchemaVersion} LastAppliedIndex={health.LastAppliedIndex} DelayedDurability={health.DelayedDurability}. No StableIndex or authority was granted.");
    }
    catch(OperationCanceledException) when(stop.IsCancellationRequested) { return 130; }
    catch(Exception exception) when(exception is SqlException or InvalidDataException or InvalidOperationException or ArgumentException or IOException)
    {
        Console.Error.WriteLine($"SQL probe failed; node remains RECOVERING/ServingEnabled=false: {exception.Message}");
        return 1;
    }
}
else if(args.Contains("--check-sql"))
{
    Console.Error.WriteLine("SQL configuration is required for --check-sql; node remains RECOVERING/ServingEnabled=false.");
    return 2;
}
else Console.WriteLine("No SQL configured; no SQL health was checked.");
if(args.Contains("--check-sql")) return 0;
Console.WriteLine("Bootstrap waiting. Press Ctrl+C to stop.");
try { await Task.Delay(Timeout.Infinite, stop.Token); }
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
return 0;
