using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string name="De6_HuyW1_Test_"+Guid.NewGuid().ToString("N");
    private readonly SqlConnectionStringBuilder builder;
    private bool created;
    internal string ConnectionString => builder.ConnectionString;
    private TestDatabase(string server)
    {
        builder=new() { DataSource=server,InitialCatalog="master",IntegratedSecurity=true,
            Encrypt=true,TrustServerCertificate=true,ConnectTimeout=15,Pooling=false };
    }
    internal static async Task<TestDatabase> Create(string server,int nodeId,bool activate=true,bool initialize=true)
    {
        var db=new TestDatabase(server);
        try
        {
            await SqlTest.Execute(db.builder.ConnectionString,$"CREATE DATABASE [{db.name}]");
            db.created=true;
            db.builder.InitialCatalog=db.name;
            if(initialize) await new SqlDatabaseBootstrap(db.ConnectionString).InitializeAsync(Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"),nodeId);
            if(activate && initialize) await SqlTest.Execute(db.ConnectionString,"UPDATE dbo.NodeState SET AcceptedEpoch=1,AcceptedLeader=1 WHERE RowId=1");
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        // This private name is generated here and only removed if CREATE in this object succeeded.
        if(!created) return;
        var admin=new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog="master" };
        await SqlTest.Execute(admin.ConnectionString,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        created=false;
    }
}
