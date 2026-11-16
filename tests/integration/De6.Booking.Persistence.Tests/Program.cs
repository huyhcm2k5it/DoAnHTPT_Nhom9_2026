using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;
using De6.Contracts;

var server = Environment.GetEnvironmentVariable("DE6_TEST_SQL_SERVER") ?? @"lpc:.\SQLEXPRESS";
var run = Guid.NewGuid().ToString("N");
var database = "De6_HuyW1_Test_" + run;
var connection = new SqlConnectionStringBuilder
{
    DataSource = server, InitialCatalog = "master", IntegratedSecurity = true,
    Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 5, Pooling = false
};
var created = false;
try
{
    await using (var admin = new SqlConnection(connection.ConnectionString))
    {
        await admin.OpenAsync();
        using var create = new SqlCommand($"CREATE DATABASE [{database}]", admin);
        await create.ExecuteNonQueryAsync();
        created = true;
    }
    connection.InitialCatalog = database;
    var bootstrap = new SqlDatabaseBootstrap(connection.ConnectionString);
    await bootstrap.InitializeAsync(Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"), 1);
    await using var sql = new SqlConnection(connection.ConnectionString);
    await sql.OpenAsync();
    using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.Seats WHERE TripId=1 AND Status='AVAILABLE'", sql);
    if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 10)
        throw new Exception("Expected exactly 10 AVAILABLE seats after initialization");
    Console.WriteLine("PASS H1 initializes ten available seats on real SQL");
    // Seed rerun must preserve database identity, not reset any data.
    await bootstrap.InitializeAsync(Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"), 1);
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(connection.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats"))==10,"Seed duplicated seats");
    Console.WriteLine("PASS H1 seed is idempotent");
    try { await bootstrap.InitializeAsync(Guid.NewGuid(),1); throw new Exception("Accepted a wrong dataset"); }
    catch(InvalidDataException) { Console.WriteLine("PASS H1 rejects another dataset"); }
    try { await bootstrap.InitializeAsync(Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009"),2); throw new Exception("Accepted a wrong node"); }
    catch(InvalidDataException) { Console.WriteLine("PASS H1 rejects another NodeId"); }
    await SqlTest.Execute(connection.ConnectionString,"UPDATE dbo.NodeState SET AcceptedEpoch=1,AcceptedLeader=1 WHERE RowId=1");
    var dataset=Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    var gate=new TestGate();
    var store=new SqlBookingStore(new(connection.ConnectionString,dataset,1),gate,new HoldFixture());
    var cmd=new CommandData(BookingAction.Hold,1,1,null);
    var identity=new CommandIdentity(dataset,Guid.Parse("22222222-2222-4222-8222-222222222222"),"A",BookingCanonical.CommandHash(dataset,"A",cmd),cmd);
    var applied=await store.DecideAndApplyAsync(identity,new(1,1),1);
    SqlTest.Require(!applied.ExistingResult && applied.Event.LogIndex==1,"Primary did not append index 1");
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(connection.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats WHERE SeatNo=1 AND Status='HELD'"))==1,"Seat was not held");
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(connection.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog"))==1,"Expected one log");
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(connection.ConnectionString,"SELECT COUNT(*) FROM dbo.RequestResults"))==1,"Expected one result");
    var retry=await store.DecideAndApplyAsync(identity,new(1,1),1);
    SqlTest.Require(retry.ExistingResult && retry.Event.EventId==applied.Event.EventId,"Retry changed event identity");
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(connection.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog"))==1,"Retry appended twice");
    Console.WriteLine("PASS H2 primary Apply is durable and retry deduplicates");
    await using var backupDb=await TestDatabase.Create(server,2);
    var backupGate=new TestGate();
    var backup=new SqlBookingStore(new(backupDb.ConnectionString,dataset,2),backupGate);
    var receipt=await backup.AppendAndApplyAsync(applied.Event,new(1,new(1,1)));
    SqlTest.Require(receipt.NodeId==2 && receipt.LogIndex==1 && receipt.EventId==applied.Event.EventId,"Wrong backup receipt");
    await backup.AppendAndApplyAsync(applied.Event,new(1,new(1,1)));
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(backupDb.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog"))==1,"Backup duplicated Apply");
    Console.WriteLine("PASS H3 backup commits event then ACKs; duplicate does not Apply again");
    gate.StableIndex=1; // Test-controlled publication, not proof of real coordinator/replication.
    var snapshot=await store.ReadStableSnapshotAsync(1);
    SqlTest.Require(snapshot.AppliedIndex==1 && snapshot.Seats.Count==10 && snapshot.Seats.Single(s=>s.SeatNo==1).Status==SeatStatus.Held,"Wrong snapshot");
    var result=await store.ReadStableResultAsync(identity.RequestId,"A",1);
    SqlTest.Require(result!=null && result.ResultPayload.Span.SequenceEqual(applied.Event.ResultPayload.Span),"Wrong stored result");
    SqlTest.Require(await store.ReadStableResultAsync(identity.RequestId,"B",1)==null,"Leaked another owner's result");
    var reservations=await store.ReadMyReservationsAsync("A",1);
    SqlTest.Require(reservations.AppliedIndex==1 && reservations.Reservations.Count==1,"Missing owned reservation");
    Console.WriteLine("PASS H4 reads snapshot/result/reservations at the same applied index and owner");
    await using var replayDb=await TestDatabase.Create(server,3,activate:false);
    var replayGate=new TestGate();
    var replayStore=new SqlBookingStore(new(replayDb.ConnectionString,dataset,3),replayGate);
    var first=BookingEventCodec.Decode(applied.Event);
    var context=new RecoveryContext(Guid.NewGuid(),dataset,1,3,2,2,0,Convert.FromHexString(first.PreviousHash),1,applied.Event.EventHash);
    replayGate.InstalledRecovery=context;
    var replayReceipt=await replayStore.ApplyReplayEntryAsync(applied.Event,context);
    SqlTest.Require(replayReceipt.AuthorityEpoch==2 && replayReceipt.EventEpoch==1 && replayReceipt.RecoveryRunId==context.RunId,"Replay changed historical authority");
    SqlTest.Require(Convert.ToInt64(await SqlTest.Scalar(replayDb.ConnectionString,"SELECT AcceptedEpoch FROM dbo.NodeState"))==0,"Replay granted authority");
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(replayDb.ConnectionString,"SELECT NodeId FROM dbo.NodeState"))==3,"Replay copied NodeId");
    await replayStore.ApplyReplayEntryAsync(applied.Event,context);
    SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(replayDb.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog"))==1,"Replay duplicated event");
    Console.WriteLine("PASS H3 historical replay retains event epoch/identity without granting serving rights");
    await AdvancedCases.Run(server,applied.Event);
    await ReviewRegressionCases.Run(server,applied.Event);
    await MaintenanceCases.Run(server);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL SQL integration: {exception.GetType().Name}: {exception.Message}");
    return 1;
}
finally
{
    // Only this run's newly created database is eligible for deletion.
    if (created && database == "De6_HuyW1_Test_" + run)
    {
        connection.InitialCatalog = "master";
        await using var admin = new SqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
