using De6.Booking.Persistence;
using De6.Contracts;

internal static class ReviewRegressionCases
{
    private static readonly Guid Dataset=Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    private static readonly DateTime ExactUtc=new DateTime(2026,11,14,1,2,3,DateTimeKind.Utc).AddTicks(1234567);
    internal static async Task Run(string server,PersistedEvent original)
    {
        var failures=new List<string>(); var passed=0;
        async Task Case(string name,Func<Task> test)
        {
            try {await test();passed++;Console.WriteLine("PASS regression "+name);}
            catch(Exception e) {failures.Add(name);Console.Error.WriteLine($"FAIL regression {name}: {e.GetType().Name}: {e.Message}");}
        }
        async Task MustCancel(Func<Task> call)
        {
            try {await call();throw new Exception("Cancelled call ran");}
            catch(OperationCanceledException) { }
        }
        async Task Refuse(SqlDatabaseBootstrap bootstrap)
        {
            try {await bootstrap.InitializeAsync(Dataset,1);throw new Exception("Unsafe bootstrap accepted");}
            catch(InvalidDataException) { }
        }
        async Task Ticks(string cs,string query)
        {
            var value=await SqlTest.Scalar(cs,query);
            SqlTest.Require(value is DateTime time && time.Ticks==ExactUtc.Ticks,"datetime2(7) ticks were changed");
        }
        await Case("timestamp ticks survive primary, append, replay, CONFIRM and CANCEL",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            var gate=new TestGate(); var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new FixedTimeFixture());
            var first=await store.DecideAndApplyAsync(AdvancedCases.Cmd(),new(1,1),1);
            await Ticks(db.ConnectionString,"SELECT CreatedUtc FROM dbo.Reservations WHERE SeatNo=1");
            gate.StableIndex=1;
            var res=BookingEventCodec.Decode(first.Event).Result.ReservationId;
            var second=await store.DecideAndApplyAsync(AdvancedCases.Cmd(BookingAction.Confirm,reservation:res),new(1,1),2);
            await Ticks(db.ConnectionString,"SELECT CreatedUtc FROM dbo.Reservations WHERE SeatNo=1");
            await Ticks(db.ConnectionString,"SELECT SoldUtc FROM dbo.Tickets WHERE SeatNo=1");
            SqlTest.Require(BookingEventCodec.Decode(second.Event).Decision.ReservationAfter!.CreatedUtc==ExactUtc,"CONFIRM changed historical CreatedUtc");
            gate.StableIndex=2;
            var third=await store.DecideAndApplyAsync(AdvancedCases.Cmd(seat:2),new(1,1),3);
            gate.StableIndex=3;
            var res2=BookingEventCodec.Decode(third.Event).Result.ReservationId;
            var fourth=await store.DecideAndApplyAsync(AdvancedCases.Cmd(BookingAction.Cancel,2,res2),new(1,1),4);
            SqlTest.Require(BookingEventCodec.Decode(fourth.Event).Decision.ReservationAfter!.CreatedUtc==ExactUtc,"CANCEL changed historical CreatedUtc");
            var entries=new[]{first.Event,second.Event,third.Event,fourth.Event};
            await using var backupDb=await TestDatabase.Create(server,2);
            var backup=new SqlBookingStore(new(backupDb.ConnectionString,Dataset,2),new TestGate());
            foreach(var entry in entries) await backup.AppendAndApplyAsync(entry,new(1,new(1,1)));
            await Ticks(backupDb.ConnectionString,"SELECT CreatedUtc FROM dbo.Reservations WHERE SeatNo=2");
            await Ticks(backupDb.ConnectionString,"SELECT SoldUtc FROM dbo.Tickets WHERE SeatNo=1");
            await using var replayDb=await TestDatabase.Create(server,3,activate:false);
            var context=new RecoveryContext(Guid.NewGuid(),Dataset,1,3,2,2,0,
                Convert.FromHexString(BookingEventCodec.Decode(first.Event).PreviousHash),4,fourth.Event.EventHash);
            var replay=new SqlBookingStore(new(replayDb.ConnectionString,Dataset,3),new TestGate{InstalledRecovery=context});
            foreach(var entry in entries) await replay.ApplyReplayEntryAsync(entry,context);
            await Ticks(replayDb.ConnectionString,"SELECT CreatedUtc FROM dbo.Reservations WHERE SeatNo=2");
            await Ticks(replayDb.ConnectionString,"SELECT SoldUtc FROM dbo.Tickets WHERE SeatNo=1");
        });
        await Case("pre-cancelled primary call leaves gate healthy and permits next operation",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            var gate=new TestGate(); var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture());
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            await MustCancel(async()=>{await store.DecideAndApplyAsync(AdvancedCases.Cmd(),new(1,1),1,cancellation.Token);});
            SqlTest.Require(!gate.Blocked && gate.Admissions==0,"Cancellation admitted work or blocked healthy gate");
            SqlTest.Require(Convert.ToInt64(await SqlTest.Scalar(db.ConnectionString,"SELECT LastAppliedIndex FROM dbo.NodeState"))==0,"Cancelled call changed SQL");
            await store.DecideAndApplyAsync(AdvancedCases.Cmd(),new(1,1),1);
        });
        await Case("pre-cancelled snapshot leaves healthy gate and subsequent read works",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            var gate=new TestGate();var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate);
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            await MustCancel(async()=>{await store.ReadStableSnapshotAsync(0,cancellation.Token);});
            SqlTest.Require(!gate.Blocked && gate.Admissions==0,"Cancelled read changed gate");
            SqlTest.Require((await store.ReadStableSnapshotAsync(0)).Seats.Count==10,"Subsequent healthy read failed");
        });
        await Case("all other pre-cancelled store APIs avoid admission",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            var gate=new TestGate();var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate);
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();var token=cancellation.Token;
            var id=Guid.NewGuid();
            var context=new RecoveryContext(Guid.NewGuid(),Dataset,1,3,2,2,0,new byte[32],1,original.EventHash);
            await MustCancel(async()=>{await store.ReadStableResultAsync(id,"A",0,token);});
            await MustCancel(async()=>{await store.ReadMyReservationsAsync("A",0,token);});
            await MustCancel(async()=>{await store.AppendAndApplyAsync(original,new(1,new(1,1)),token);});
            await MustCancel(async()=>{await store.ApplyReplayEntryAsync(original,context,token);});
            await MustCancel(async()=>{await store.InspectLocalRequestAsync(id,token);});
            SqlTest.Require(gate.Admissions==0 && !gate.Blocked,"Cancelled API acquired a gate");
        });
        await Case("unrecognized populated database is refused and sentinel preserved",async()=>
        {
            await using var db=await TestDatabase.Create(server,1,activate:false,initialize:false);
            await SqlTest.Execute(db.ConnectionString,"CREATE TABLE dbo.UserData(Id int NOT NULL); INSERT dbo.UserData VALUES(41)");
            await Refuse(new(db.ConnectionString));
            SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT Id FROM dbo.UserData"))==41,"Bootstrap changed foreign data");
            SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"))==1,"Bootstrap added tables to foreign DB");
        });
        foreach(var marker in new[]{"De6.SchemaChecksum","De6.SchemaVersion"})
        await Case("changed "+marker+" is refused without reseeding",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            var value=marker.EndsWith("Checksum",StringComparison.Ordinal)?"N'tampered'":"2";
            await SqlTest.Execute(db.ConnectionString,$"EXEC sys.sp_updateextendedproperty @name=N'{marker}',@value={value}");
            await Refuse(new(db.ConnectionString));
            SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats"))==10,"Refusal changed seed data");
            SqlTest.Require(Convert.ToInt64(await SqlTest.Scalar(db.ConnectionString,"SELECT AcceptedEpoch FROM dbo.NodeState"))==1,"Refusal reset authority");
            var expected=marker.EndsWith("Checksum",StringComparison.Ordinal)?"tampered":"2";
            SqlTest.Require(Convert.ToString(await SqlTest.Scalar(db.ConnectionString,$"SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'{marker}'"))==expected,"Refusal rewrote marker");
        });
        await Case("missing schema object is refused without repairing silently",async()=>
        {
            await using var db=await TestDatabase.Create(server,1);
            await SqlTest.Execute(db.ConnectionString,"DROP TABLE dbo.RequestResults");
            await Refuse(new(db.ConnectionString));
            SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats"))==10,"Refusal changed seats");
            SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM sys.tables WHERE name='RequestResults'"))==0,"Bootstrap silently repaired altered schema");
        });
        Console.WriteLine($"SQL review regressions: {passed} passed, {failures.Count} failed");
        if(failures.Count>0) throw new Exception("Review regressions failed: "+string.Join("; ",failures));
    }
    private sealed class FixedTimeFixture : IBookingDecisionEngine
    {
        public BookingDecision Decide(CommandIdentity command,BookingState state,long nextIndex,DateTime utcNow)
            =>new HoldFixture().Decide(command,state,nextIndex,ExactUtc);
    }
}
