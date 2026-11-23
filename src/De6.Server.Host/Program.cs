using De6.Cluster.Control;
using De6.Contracts;
using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;
using De6.Server.Host;

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
var control = new ClusterControl(options.NodeId, options.DatasetId);
var state = control.Snapshot;
Console.WriteLine($"S{options.NodeId} DatasetId={options.DatasetId:D} Role={state.Role.ToString().ToUpperInvariant()} ServingEnabled={state.ServingEnabled.ToString().ToLowerInvariant()} StableIndex={state.StableIndex}");
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
        var storeOptions=SqlPersistenceComposition.CreateStoreOptions(options);
        var health=await new SqlStorageHealth().ProbeAsync(storeOptions,stop.Token);
        var persistence=HostPersistence.Compose(storeOptions, health);
        var local=persistence.Control.Snapshot;
        Console.WriteLine($"SQL local probe passed: SchemaVersion={health.SchemaVersion} LastAppliedIndex={local.LastAppliedIndex} DelayedDurability={health.DelayedDurability}. No StableIndex or authority was granted.");
        if (!args.Contains("--check-sql") && Environment.GetEnvironmentVariable("DE6_HOST_TLS_CONFIG") is { Length: > 0 } tlsPath)
        {
            await HostTlsRuntime.RunAsync(options, persistence, HostTlsOptions.Load(tlsPath), storeOptions, stop.Token);
            return 0;
        }
    }
    catch(OperationCanceledException) when(stop.IsCancellationRequested) { return 130; }
    catch(Exception exception) when(exception is SqlException or InvalidDataException or InvalidOperationException or
        ArgumentException or IOException or System.Text.Json.JsonException or FormatException or
        System.Security.Cryptography.CryptographicException or System.Net.Sockets.SocketException)
    {
        Console.Error.WriteLine($"Host startup failed: {exception.Message}");
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
Console.WriteLine("Bootstrap waiting without listeners. Set DE6_HOST_TLS_CONFIG to enable TLS listeners. Press Ctrl+C to stop.");
try { await Task.Delay(Timeout.Infinite, stop.Token); }
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
return 0;
