using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using De6.Booking.Persistence;
using De6.Contracts;
using Microsoft.Data.SqlClient;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "DoAnHTPT_Nhom9_2026.sln"))) root = root.Parent;
if (root is null) throw new InvalidOperationException("Run this harness inside the repository.");
var suite = new HostCases(root.FullName);
await suite.Run();
await CompositionCases.Run(suite);
if (args.SequenceEqual(new[] { "--sql" })) await SqlProbeCases.Run(suite);
else if (args.Length != 0) throw new ArgumentException("Usage: De6.Host.Persistence.Tests [--sql]");
Console.WriteLine($"Host/persistence failures: {suite.Failures}");
return suite.Failures == 0 ? 0 : 1;

internal sealed class HostCases(string root)
{
    internal int Failures { get; private set; }
    internal string Root => root;
    internal JsonObject Configuration(string? database = "de_6")
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root,"config/local/s1.json")))!.AsObject();
        config.Remove("sql");
        if (database is not null)
            config["sql"] = new JsonObject { ["server"] = Environment.GetEnvironmentVariable("DE6_TEST_SQL_SERVER") ?? "lpc:.\\SQLEXPRESS", ["database"] = database, ["connectTimeoutSeconds"] = 2 };
        return config;
    }
    internal async Task Run()
    {
        await Check("legacy configuration remains valid without SQL", () =>
        {
            using var file = new ConfigFile(Configuration(null));
            Require(ConfigurationFile.ReadServer(file.Path).NodeId == 1,"Legacy configuration was rejected.");
            return Task.CompletedTask;
        });
        await Check("local SQL configuration is accepted", () =>
        {
            using var file = new ConfigFile(Configuration());
            Require(ConfigurationFile.ReadServer(file.Path).NodeId == 1,"SQL configuration was rejected.");
            return Task.CompletedTask;
        });
        foreach (var (field,value) in new (string,JsonNode?)[] {
            ("server","tcp:192.168.1.11,1433"),("server","lpc:other-machine\\SQLEXPRESS"),
            ("server","lpc:.\\SQLEXPRESS;User ID=sa"),("server",null),
            ("database","master"),("database","personal_database"),("database","de_6;Password=x"),("database",null),
            ("connectTimeoutSeconds",0),("connectTimeoutSeconds",31), ("password","secret"), ("userId","sa") })
            await Check("reject unsafe SQL setting "+field+"="+value, () =>
            {
                var config=Configuration(); config["sql"]![field]=value?.DeepClone();
                using var file=new ConfigFile(config);
                Reject(()=>ConfigurationFile.ReadServer(file.Path));
                return Task.CompletedTask;
            });
        await Check("unknown top-level field stays rejected", () =>
        {
            var config=Configuration(null); config["sqll"]=new JsonObject();
            using var file=new ConfigFile(config); Reject(()=>ConfigurationFile.ReadServer(file.Path));
            return Task.CompletedTask;
        });
        await Check("--check remains offline with configured SQL", async () =>
        {
            var config=Configuration("De6_Missing_"+Guid.NewGuid().ToString("N"));
            config["sql"]!["server"]="lpc:.\\De6MissingInstance";
            var result=await Host(config,"--check");
            Require(result.Exit==0 && result.Output.Contains("Role=RECOVERING ServingEnabled=false"),result.ToString());
        });
        await Check("--check-sql requires explicit SQL configuration", async () =>
        {
            var result=await Host(Configuration(null),"--check-sql");
            Require(result.Exit==2 && result.Error.Contains("SQL",StringComparison.OrdinalIgnoreCase),result.ToString());
        });
        await Check("unavailable SQL exits closed", async () =>
        {
            var config=Configuration(); config["sql"]!["server"]="lpc:.\\De6MissingInstance";
            var result=await Host(config,"--check-sql");
            Require(result.Exit==1 && result.Output.Contains("Role=RECOVERING ServingEnabled=false"),result.ToString());
        });
        await Check("normal startup probes configured SQL before waiting", async () =>
        {
            var config=Configuration(); config["sql"]!["server"]="lpc:.\\De6MissingInstance";
            var result=await Host(config);
            Require(result.Exit==1 && result.Output.Contains("ServingEnabled=false"),result.ToString());
        });
    }
    internal async Task Check(string name,Func<Task> run)
    {
        try { await run(); Console.WriteLine("PASS "+name); }
        catch(Exception e) { Failures++; Console.Error.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
    }
    internal async Task<HostResult> Host(JsonObject config,string? mode=null)
    {
        using var file=new ConfigFile(config);
        var configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var host=Path.Combine(root,"src/De6.Server.Host/bin",configuration,"net10.0/De6.Server.Host.dll");
        var dotnet=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"../../..","dotnet.exe"));
        var start=new ProcessStartInfo(dotnet) { RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=root };
        foreach(var argument in new[]{host,"--config",file.Path}) start.ArgumentList.Add(argument);
        if(mode is not null) start.ArgumentList.Add(mode);
        using var process=Process.Start(start) ?? throw new InvalidOperationException("Could not start Host.");
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch(OperationCanceledException) { process.Kill(entireProcessTree:true); throw new Exception("Host did not exit within 20 seconds."); }
        return new(process.ExitCode,await output,await error);
    }
    internal static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Reject(Action run)
    {
        try { run(); }
        catch(Exception e) when(e is InvalidDataException or System.Text.Json.JsonException) { return; }
        throw new Exception("Unsafe configuration accepted.");
    }
}
internal static class CompositionCases
{
    internal static async Task Run(HostCases suite)
    {
        await suite.Check("composition uses local Windows auth and an explicit application database",()=>
        {
            using var file=new ConfigFile(suite.Configuration("De6_S2"));
            var options=SqlPersistenceComposition.CreateStoreOptions(ConfigurationFile.ReadServer(file.Path));
            var sql=new SqlConnectionStringBuilder(options.ConnectionString);
            HostCases.Require(sql.DataSource==(Environment.GetEnvironmentVariable("DE6_TEST_SQL_SERVER") ?? "lpc:.\\SQLEXPRESS") && sql.InitialCatalog=="De6_S2" &&
                sql.IntegratedSecurity && sql.UserID=="" && sql.Password=="" && !sql.PersistSecurityInfo && sql.ConnectTimeout==2,
                "Composition lost local identity/authentication settings.");
            return Task.CompletedTask;
        });
        await suite.Check("composition requires a supplied gate and decision engine",()=>
        {
            using var file=new ConfigFile(suite.Configuration());
            var options=ConfigurationFile.ReadServer(file.Path);
            RejectNull(()=>SqlPersistenceComposition.CreateStore(options,null!,new NeverDecision()));
            RejectNull(()=>SqlPersistenceComposition.CreateStore(options,new ClosedGate(),null!));
            return Task.CompletedTask;
        });
        await suite.Check("composed store honors the supplied closed authority gate before SQL",async()=>
        {
            var config=suite.Configuration(); config["sql"]!["server"]="lpc:.\\De6MissingInstance";
            using var file=new ConfigFile(config); var options=ConfigurationFile.ReadServer(file.Path);
            var store=SqlPersistenceComposition.CreateStore(options,new ClosedGate(),new NeverDecision());
            var command=new CommandData(BookingAction.Hold,1,1,null);
            var identity=new CommandIdentity(options.DatasetId,Guid.NewGuid(),"A",BookingCanonical.CommandHash(options.DatasetId,"A",command),command);
            try { await store.DecideAndApplyAsync(identity,new AcceptedAuthority(1,1),1); }
            catch(StoreRejectedException e) when(e.Code==StoreRejection.NotReady) { return; }
            throw new Exception("Composed store bypassed the supplied authority gate.");
        });
        await suite.Check("pre-cancelled SQL probe exits before opening a connection",async()=>
        {
            using var cancel=new CancellationTokenSource(); cancel.Cancel();
            try { await new SqlStorageHealth().ProbeAsync(new("not a connection string",Guid.NewGuid(),1),cancel.Token); }
            catch(OperationCanceledException) when(cancel.IsCancellationRequested) { return; }
            throw new Exception("Probe ignored cancellation before SQL access.");
        });
    }
    private static void RejectNull(Action action)
    {
        try { action(); } catch(ArgumentNullException) { return; }
        throw new Exception("Composition accepted a missing authority/decision adapter.");
    }
    private sealed class NeverDecision : IBookingDecisionEngine
    {
        public BookingDecision Decide(CommandIdentity command,BookingState state,long index,DateTime utcNow)
            => throw new Exception("Decision engine must not be reached with a closed gate.");
    }
    // Deliberately always closed. This is a test-only adapter, never an authority grant.
    private sealed class ClosedGate : IStoreAccessGate
    {
        private static IDisposable Closed()=>throw new StoreRejectedException(StoreRejection.NotReady);
        public IDisposable EnterPrimary(AcceptedAuthority authority,long index)=>Closed();
        public IDisposable EnterAppend(AuthenticatedPeerAuthority peer)=>Closed();
        public IDisposable EnterReplay(RecoveryContext context)=>Closed();
        public IDisposable EnterSnapshot(long index)=>Closed();
        public IDisposable EnterRecoveryInspection()=>Closed();
        public IDisposable EnterMaintenance(long index)=>Closed();
        public void Block(string reason) { }
    }
}
internal sealed record HostResult(int Exit,string Output,string Error);
internal sealed class ConfigFile : IDisposable
{
    internal string Path { get; }=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"de6-host-"+Guid.NewGuid().ToString("N")+".json");
    internal ConfigFile(JsonObject config) => File.WriteAllText(Path,config.ToJsonString());
    public void Dispose() => File.Delete(Path);
}
