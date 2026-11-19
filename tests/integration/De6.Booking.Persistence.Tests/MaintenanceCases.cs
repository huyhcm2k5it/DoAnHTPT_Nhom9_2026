using De6.Booking.Persistence;
using De6.Contracts;

internal static class MaintenanceCases
{
    private static readonly Guid Dataset=Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    internal static async Task Run(string server)
    {
        await using var db=await TestDatabase.Create(server,1);
        var gate=new TestGate();var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture());
        var first=await store.DecideAndApplyAsync(AdvancedCases.Cmd(),new(1,1),1);
        gate.StableIndex=1;
        var reservation=BookingEventCodec.Decode(first.Event).Result.ReservationId;
        var second=await store.DecideAndApplyAsync(AdvancedCases.Cmd(BookingAction.Confirm,reservation:reservation),new(1,1),2);
        var audit=await store.AuditLocalAsync();
        SqlTest.Require(audit.LocalAppliedIndex==2 && audit.MatchesHistory && audit.Violations.Count==0,"Valid data failed oracle");
        Console.WriteLine("PASS H5 local oracle validates log and SQL projection");
        try {await store.RebuildProjectionAsync(2);throw new Exception("Rebuild accepted without maintenance");}
        catch(StoreRejectedException e) when(e.Code==StoreRejection.NotReady) { }
        gate.Maintenance=true;
        var frozen=await store.GetFrozenPrefixAsync(2);
        await using var backupDb=await TestDatabase.Create(server,2);
        var backupGate=new TestGate();
        var backup=new SqlBookingStore(new(backupDb.ConnectionString,Dataset,2),backupGate);
        await backup.AppendAndApplyAsync(first.Event,new(1,new(1,1)));
        await backup.AppendAndApplyAsync(second.Event,new(1,new(1,1)));
        backupGate.Maintenance=true;
        var other=await backup.GetFrozenPrefixAsync(2);
        SqlTest.Require(frozen.BusinessDigest.Span.SequenceEqual(other.BusinessDigest.Span) && frozen.HeadHash.Span.SequenceEqual(other.HeadHash.Span),"Replica digest mismatch");
        Console.WriteLine("PASS H5 frozen digest is identical across different NodeIds");
        var primaryHistory=await store.GetVerifiedRecoveryHistoryAsync(2);
        var backupHistory=await backup.GetVerifiedRecoveryHistoryAsync(2);
        primaryHistory.Validate();backupHistory.Validate();
        var allCheckpointsMatch=primaryHistory.Checkpoints.Count==3 && backupHistory.Checkpoints.Count==3;
        for(var index=0;allCheckpointsMatch && index<primaryHistory.Checkpoints.Count;index++)
            allCheckpointsMatch=primaryHistory.Checkpoints[index].HeadHash.Span.SequenceEqual(backupHistory.Checkpoints[index].HeadHash.Span) &&
                primaryHistory.Checkpoints[index].BusinessDigest.Span.SequenceEqual(backupHistory.Checkpoints[index].BusinessDigest.Span);
        SqlTest.Require(allCheckpointsMatch,"Verified recovery history did not expose matching audited checkpoints");
        Console.WriteLine("PASS A4 frozen SQL history provides verified hash/digest at every prefix");
        await SqlTest.Execute(db.ConnectionString,"UPDATE dbo.RequestResults SET ResultPayloadBytes=0x7B7D");
        SqlTest.Require(!(await store.AuditLocalAsync()).MatchesHistory,"Digest missed altered result bytes");
        await SqlTest.Execute(db.ConnectionString,"DELETE dbo.Tickets");
        var damaged=await store.AuditLocalAsync();
        SqlTest.Require(!damaged.MatchesHistory && damaged.Violations.Count>0,"Oracle missed corrupt projection/results");
        var failing=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,observer:new BeforeRebuildFailure());
        try{await failing.RebuildProjectionAsync(2);throw new Exception("Rebuild fault was not injected");}
        catch(StorageUncertainException) { }
        SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets"))==0,"Failed rebuild did not roll back");
        var rebuilt=await store.RebuildProjectionAsync(2);
        SqlTest.Require(rebuilt.BusinessDigest.Span.SequenceEqual(frozen.BusinessDigest.Span),"Rebuild changed historical data");
        SqlTest.Require(Convert.ToInt64(await SqlTest.Scalar(db.ConnectionString,"SELECT AcceptedEpoch FROM dbo.NodeState"))==1 &&
            Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT NodeId FROM dbo.NodeState"))==1,"Rebuild changed local identity/authority");
        var bytes=(byte[])(await SqlTest.Scalar(db.ConnectionString,"SELECT PayloadBytes FROM dbo.OperationLog WHERE LogIndex=2"))!;
        SqlTest.Require(bytes.AsSpan().SequenceEqual(second.Event.PayloadBytes.Span),"Rebuild modified log bytes");
        Console.WriteLine("PASS H5 oracle detects corruption and rebuild repairs only derived data");
        try {await store.GetFrozenPrefixAsync(1);throw new Exception("Historical K treated as current snapshot");}
        catch(StoreRejectedException e) when(e.Code==StoreRejection.IndexMismatch) { }
        await SqlTest.Execute(db.ConnectionString,"UPDATE dbo.OperationLog SET EventHash=0x0000000000000000000000000000000000000000000000000000000000000000 WHERE LogIndex=1");
        try {await store.RebuildProjectionAsync(2);throw new Exception("Rebuild accepted corrupt immutable log");}
        catch(StoreRejectedException e) when(e.Code==StoreRejection.PrefixConflict) { }
        SqlTest.Require(Convert.ToInt32(await SqlTest.Scalar(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets"))==1,"Failed rebuild destroyed projection");
        Console.WriteLine("PASS H5 refuses wrong K/corrupt log before touching data");
    }
    private sealed class BeforeRebuildFailure : IApplyObserver
    {
        public Task BeforeCommitAsync(PersistedEvent entry)=>throw new IOException("Injected rebuild before commit");
        public Task AfterCommitAsync(PersistedEvent entry)=>Task.CompletedTask;
    }
}
