using System.Text.RegularExpressions;
using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;
using De6.Contracts;

if(args.Length==0 || args[0] is not ("init" or "status" or "audit")) return Usage();
try
{
    var values=new Dictionary<string,string>(StringComparer.Ordinal);
    var create=false;
    for(var i=1;i<args.Length;i++)
    {
        if(args[i]=="--create") {create=true;continue;}
        if(args[i] is not ("--server" or "--database" or "--node" or "--dataset") || i+1>=args.Length || !values.TryAdd(args[i],args[++i]))
            return Usage();
    }
    if(!values.TryGetValue("--database",out var database) ||
        (database!="de_6" && !Regex.IsMatch(database,@"\ADe6_[A-Za-z0-9_]{1,59}\z",RegexOptions.CultureInvariant)))
        throw new ArgumentException("Database must be de_6 or start with De6_ and contain only ASCII letters, digits and underscore (max 63 characters)");
    var server=values.GetValueOrDefault("--server",@"lpc:.\SQLEXPRESS");
    var computer=server.StartsWith("lpc:",StringComparison.OrdinalIgnoreCase)?server[4..]:server;
    computer=computer.Split('\\')[0];
    if(!new[]{".","localhost","(local)",Environment.MachineName}.Contains(computer,StringComparer.OrdinalIgnoreCase))
        throw new ArgumentException("This tool is for SQL on the local server machine only");
    var builder=new SqlConnectionStringBuilder
    {
        DataSource=server,InitialCatalog="master",IntegratedSecurity=true,Encrypt=true,
        TrustServerCertificate=true,ConnectTimeout=5,Pooling=false
    };
    if(args[0]=="init")
    {
        if(!values.TryGetValue("--node",out var nodeText) || !int.TryParse(nodeText,out var node) || node is <1 or >3 ||
           !values.TryGetValue("--dataset",out var dataText) || !Guid.TryParse(dataText,out var dataset) || dataset==Guid.Empty)
            return Usage();
        await using(var admin=new SqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            using var check=new SqlCommand("SELECT DB_ID(@name)",admin);
            check.Parameters.AddWithValue("@name",database);
            var exists=await check.ExecuteScalarAsync()!=DBNull.Value;
            if(!exists)
            {
                if(!create) throw new InvalidOperationException("Database does not exist; use --create to create a new empty database");
                using var ddl=new SqlCommand($"CREATE DATABASE [{database}]",admin);
                await ddl.ExecuteNonQueryAsync();
            }
        }
        builder.InitialCatalog=database;
        await new SqlDatabaseBootstrap(builder.ConnectionString).InitializeAsync(dataset,node);
        Console.WriteLine($"Initialized {database}: NodeId={node}, DatasetId={dataset:D}");
        Console.WriteLine("Seed does not grant authority, open serving rights or produce FINAL.");
    }
    else
    {
        if(create) return Usage();
        builder.InitialCatalog=database;
    }
    await using var sql=new SqlConnection(builder.ConnectionString);
    await sql.OpenAsync();
    using var status=new SqlCommand("SELECT NodeId,DatasetId,AcceptedEpoch,AcceptedLeader,LastAppliedIndex,SchemaVersion,(SELECT COUNT(*) FROM dbo.Seats),(SELECT COUNT(*) FROM dbo.Tickets) FROM dbo.NodeState WHERE RowId=1",sql);
    await using var row=await status.ExecuteReaderAsync();
    if(!await row.ReadAsync()) throw new InvalidDataException("NodeState missing");
    Console.WriteLine($"Database={database}; NodeId={row.GetInt32(0)}; DatasetId={row.GetGuid(1):D}; AcceptedEpoch={row.GetInt64(2)}; AcceptedLeader={(row.IsDBNull(3)?"NULL":row.GetInt32(3).ToString())}; LastAppliedIndex={row.GetInt64(4)}; SchemaVersion={row.GetInt32(5)}; Seats={row.GetInt32(6)}; Tickets={row.GetInt32(7)}");
    if(args[0]=="audit")
    {
        var node=row.GetInt32(0);var dataset=row.GetGuid(1);
        await row.DisposeAsync();
        var store=new SqlBookingStore(new(builder.ConnectionString,dataset,node),new InspectionOnlyGate());
        var audit=await store.AuditLocalAsync();
        Console.WriteLine($"LOCAL ONLY K={audit.LocalAppliedIndex} HeadHash={Convert.ToHexStringLower(audit.HeadHash.Span)} Digest={Convert.ToHexStringLower(audit.BusinessDigest.Span)} MatchesHistory={audit.MatchesHistory}");
        foreach(var violation in audit.Violations) Console.WriteLine($"VIOLATION {violation.Invariant} Count={violation.Count}");
        Console.WriteLine("Local oracle cannot prove Stable, FINAL, fencing or cluster fault tolerance.");
        return audit.MatchesHistory?0:1;
    }
    return 0;
}
catch(Exception e) when(e is ArgumentException or InvalidDataException or InvalidOperationException or SqlException or StoreRejectedException or StorageUncertainException)
{
    Console.Error.WriteLine($"SQL setup failed: {e.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("De6.Sql.Tool init --database de_6 --node 1|2|3 --dataset GUID [--server local-instance] [--create]");
    Console.Error.WriteLine("De6.Sql.Tool status --database de_6 [--server local-instance]");
    Console.Error.WriteLine("De6.Sql.Tool audit --database de_6 [--server local-instance]");
    return 2;
}

// Administration can read local evidence. This adapter grants no write, replay or maintenance rights.
sealed class InspectionOnlyGate : IStoreAccessGate
{
    public IDisposable EnterRecoveryInspection()=>new Lease();
    public IDisposable EnterPrimary(AcceptedAuthority authority,long expectedNextIndex)=>Denied();
    public IDisposable EnterAppend(AuthenticatedPeerAuthority peer)=>Denied();
    public IDisposable EnterReplay(AuthenticatedPeerIdentity authenticatedSource,RecoveryContext context)=>Denied();
    public IDisposable EnterSnapshot(long expectedIndex)=>Denied();
    public IDisposable EnterMaintenance(long expectedIndex)=>Denied();
    public void Block(string reason) { }
    private static IDisposable Denied()=>throw new StoreRejectedException(StoreRejection.NotReady);
    private sealed class Lease : IDisposable {public void Dispose() { }}
}
