using De6.Booking.Persistence;
using De6.Contracts;
using Microsoft.Data.SqlClient;

internal static class SqlProbeCases
{
    internal static async Task Run(HostCases suite)
    {
        await using var database=await ProbeDatabase.Create();
        await suite.Check("read-only SQL probe accepts seeded local identity and preserves all rows",async()=>
        {
            var before=await database.Snapshot();
            var result=await suite.Host(suite.Configuration(database.Name),"--check-sql");
            HostCases.Require(result.Exit==0 && result.Output.Contains("LastAppliedIndex=0") &&
                result.Output.Contains("Role=RECOVERING ServingEnabled=false"),result.ToString());
            HostCases.Require(before==await database.Snapshot(),"Probe changed SQL state.");
        });
        await suite.Check("SQL probe requests NodeState update lock before reading the operation log",async()=>
        {
            await using var connection=new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            using(var command=new SqlCommand("SELECT NodeId FROM dbo.NodeState WITH(UPDLOCK,HOLDLOCK) WHERE RowId=1",connection,transaction))
                _=await command.ExecuteScalarAsync();

            var probe=new SqlStorageHealth().ProbeAsync(
                new(new SqlConnectionStringBuilder(database.ConnectionString) { ApplicationName=ProbeDatabase.ProbeApplicationName }.ConnectionString,
                    Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"),1));
            var observed=false;
            try
            {
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(DateTime.UtcNow<deadline && !probe.IsCompleted)
                {
                    var waiting=Convert.ToInt32(await database.Scalar("SELECT COUNT(*) FROM sys.dm_tran_locks l JOIN sys.dm_exec_sessions s ON s.session_id=l.request_session_id WHERE l.resource_database_id=DB_ID() AND l.request_status='WAIT' AND l.request_mode='U' AND s.program_name=N'"+ProbeDatabase.ProbeApplicationName+"'"));
                    if(waiting>0) { observed=true; break; }
                    await Task.Delay(50);
                }
            }
            finally { await transaction.RollbackAsync(); }
            var report=await probe.WaitAsync(TimeSpan.FromSeconds(10));
            HostCases.Require(observed,"Probe did not wait for the writer-compatible NodeState U lock. It may have taken S(NodeState) and continued out of order.");
            HostCases.Require(report.LastAppliedIndex==0,"Probe returned an unexpected log index.");
        });
        foreach(var field in new[]{"nodeId","datasetId"})
            await suite.Check("SQL probe rejects mismatched "+field+" without mutation",async()=>
            {
                var config=suite.Configuration(database.Name);
                if(field=="nodeId") { config[field]=2; config["dataDirectory"]="data/servers/s2"; }
                else config[field]=Guid.NewGuid().ToString("D");
                var before=await database.Snapshot();
                var result=await suite.Host(config,"--check-sql");
                HostCases.Require(result.Exit==1,result.ToString());
                HostCases.Require(before==await database.Snapshot(),"Rejected probe changed SQL state.");
            });
        await suite.Check("SQL probe never creates a missing database",async()=>
        {
            var name="De6_HostMissing_"+Guid.NewGuid().ToString("N");
            var result=await suite.Host(suite.Configuration(name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(Convert.ToInt32(await database.Scalar("SELECT COUNT(*) FROM sys.databases WHERE name='"+name+"'"))==0,"Probe created database.");
        });
        await suite.Check("SQL probe never seeds an empty database",async()=>
        {
            await using var empty=await ProbeDatabase.Create(initialize:false);
            var result=await suite.Host(suite.Configuration(empty.Name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(Convert.ToInt32(await empty.Scalar("SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"))==0,"Probe created tables.");
        });
        await suite.Check("SQL probe accepts a persisted nonzero local log but keeps Host recovering",async()=>
        {
            await database.AppendFixture(suite.Root);
            var before=await database.Snapshot();
            var result=await suite.Host(suite.Configuration(database.Name),"--check-sql");
            HostCases.Require(result.Exit==0 && result.Output.Contains("LastAppliedIndex=1") &&
                result.Output.Contains("Role=RECOVERING ServingEnabled=false"),result.ToString());
            HostCases.Require(before==await database.Snapshot(),"Probe changed a nonempty database.");
        });
        await suite.Check("SQL probe rejects a head hash that differs from the persisted tail",async()=>
        {
            await database.Execute("UPDATE dbo.NodeState SET HeadHash=0x0000000000000000000000000000000000000000000000000000000000000000 WHERE RowId=1");
            var before=await database.Snapshot();
            var result=await suite.Host(suite.Configuration(database.Name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(before==await database.Snapshot(),"Probe repaired the head hash.");
        });
        await suite.Check("SQL probe rejects schema checksum mismatch without repairing",async()=>
        {
            await database.Execute("EXEC sys.sp_updateextendedproperty @name=N'De6.SchemaChecksum',@value=N'corrupt'");
            var before=await database.Snapshot();
            var result=await suite.Host(suite.Configuration(database.Name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(before==await database.Snapshot(),"Probe repaired schema metadata.");
        });
        await using var durability=await ProbeDatabase.Create();
        await suite.Check("SQL probe rejects forced delayed durability without changing authority",async()=>
        {
            await durability.Execute($"ALTER DATABASE [{durability.Name}] SET DELAYED_DURABILITY=FORCED");
            var before=await durability.Snapshot();
            var result=await suite.Host(suite.Configuration(durability.Name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(before==await durability.Snapshot(),"Probe changed authority/data.");
        });
        await using var index=await ProbeDatabase.Create();
        await suite.Check("SQL probe rejects LastAppliedIndex ahead of local log",async()=>
        {
            await index.Execute("UPDATE dbo.NodeState SET LastAppliedIndex=1 WHERE RowId=1");
            var before=await index.Snapshot();
            var result=await suite.Host(suite.Configuration(index.Name),"--check-sql");
            HostCases.Require(result.Exit==1,result.ToString());
            HostCases.Require(before==await index.Snapshot(),"Probe changed local index.");
        });
    }
}

// Only generated names whose CREATE succeeded are ever dropped by this fixture.
internal sealed class ProbeDatabase : IAsyncDisposable
{
    internal string Name { get; }="De6_HostProbe_Test_"+Guid.NewGuid().ToString("N");
    internal const string ProbeApplicationName="De6HostProbeFixture";
    private readonly SqlConnectionStringBuilder builder=new() { DataSource=Environment.GetEnvironmentVariable("DE6_TEST_SQL_SERVER") ?? "lpc:.\\SQLEXPRESS",InitialCatalog="master",
        IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,ConnectTimeout=15,Pooling=false };
    private bool created;
    internal string ConnectionString=>builder.ConnectionString;
    internal static async Task<ProbeDatabase> Create(bool initialize=true)
    {
        var database=new ProbeDatabase();
        try
        {
            await database.Execute($"CREATE DATABASE [{database.Name}]");
            database.created=true; database.builder.InitialCatalog=database.Name;
            if(initialize) await new SqlDatabaseBootstrap(database.ConnectionString)
                .InitializeAsync(Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"),1);
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }
    internal async Task Execute(string sql)
    {
        await using var connection=new SqlConnection(ConnectionString); await connection.OpenAsync();
        using var command=new SqlCommand(sql,connection); await command.ExecuteNonQueryAsync();
    }
    internal async Task<object?> Scalar(string sql)
    {
        await using var connection=new SqlConnection(ConnectionString); await connection.OpenAsync();
        using var command=new SqlCommand(sql,connection); return await command.ExecuteScalarAsync();
    }
    internal async Task<string> Snapshot()
    {
        var results=new List<string>();
        foreach(var (table,key) in new[]{("Trips","TripId"),("Seats","TripId,SeatNo"),("Reservations","ReservationId"),
            ("Tickets","TicketId"),("OperationLog","LogIndex"),("RequestResults","RequestId"),("NodeState","RowId")})
            results.Add(Convert.ToString(await Scalar($"SELECT (SELECT * FROM dbo.{table} ORDER BY {key} FOR JSON PATH,INCLUDE_NULL_VALUES)"))!);
        results.Add(Convert.ToString(await Scalar("SELECT (SELECT name,CONVERT(nvarchar(128),value) AS value FROM sys.extended_properties WHERE class=0 ORDER BY name FOR JSON PATH)"))!);
        return string.Join("\n",results);
    }
    internal async Task AppendFixture(string root)
    {
        var value=BookingEventCodec.Read(File.ReadAllBytes(Path.Combine(root,
            "tests/unit/De6.Persistence.Contracts.Tests/event-v1.json"))) with { WriterId=2 };
        var entry=BookingEventCodec.Encode(value);
        // Test-only authority setup on this fixture's generated DB, never on a development DB.
        await Execute("UPDATE dbo.NodeState SET AcceptedEpoch=1,AcceptedLeader=2 WHERE RowId=1");
        var store=new SqlBookingStore(new(ConnectionString,entry.DatasetId,1),new FixtureAppendGate());
        await store.AppendAndApplyAsync(entry,new AuthenticatedPeerAuthority(2,new AcceptedAuthority(1,2)));
    }
    public async ValueTask DisposeAsync()
    {
        if(!created) return;
        SqlConnection.ClearAllPools();
        builder.InitialCatalog="master";
        await Execute($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]");
        created=false;
    }
    private sealed class FixtureAppendGate : IStoreAccessGate
    {
        public IDisposable EnterAppend(AuthenticatedPeerAuthority peer)
        {
            if(peer!=new AuthenticatedPeerAuthority(2,new AcceptedAuthority(1,2))) return Closed();
            return new Lease();
        }
        private static IDisposable Closed()=>throw new StoreRejectedException(StoreRejection.NotReady);
        public IDisposable EnterPrimary(AcceptedAuthority authority,long index)=>Closed();
        public IDisposable EnterReplay(RecoveryContext context)=>Closed();
        public IDisposable EnterSnapshot(long index)=>Closed();
        public IDisposable EnterRecoveryInspection()=>Closed();
        public IDisposable EnterMaintenance(long index)=>Closed();
        public void Block(string reason) { }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
