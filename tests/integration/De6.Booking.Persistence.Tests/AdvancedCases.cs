using De6.Contracts;
using De6.Booking.Persistence;

internal static class AdvancedCases
{
    private static readonly Guid Dataset=Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    internal static CommandIdentity Cmd(BookingAction action=BookingAction.Hold,int seat=1,Guid? reservation=null,string owner="A",Guid? request=null)
    {
        var command=new CommandData(action,1,seat,reservation);
        return new(Dataset,request??Guid.NewGuid(),owner,BookingCanonical.CommandHash(Dataset,owner,command),command);
    }
    internal static async Task Run(string server,PersistedEvent original)
    {
        var count=0;
        async Task Case(string name,Func<Task> run) { await run(); count++; Console.WriteLine("PASS SQL "+name); }
        async Task Rejected(Func<Task> run,StoreRejection code)
        {
            try { await run(); throw new Exception("Expected rejection "+code); }
            catch(StoreRejectedException e) when(e.Code==code) { }
        }
        async Task<long> N(string cs,string sql)=>Convert.ToInt64(await SqlTest.Scalar(cs,sql));
        var initial=BookingEventCodec.Decode(original);

        await using(var db=await TestDatabase.Create(server,1))
        {
            var gate=new TestGate(); var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture());
            var command=Cmd(); var first=await store.DecideAndApplyAsync(command,new(1,1),1);
            await Case("blocks reads before Stable",()=>Rejected(async()=>{await store.ReadStableSnapshotAsync(1);},StoreRejection.NotReady));
            await Case("rejects wrong snapshot index even if caller gate is stale",()=>Rejected(async()=>{await store.ReadStableSnapshotAsync(0);},StoreRejection.IndexMismatch));
            await Case("concurrent retry has one event",async()=>
            {
                var retries=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>Task.Run(()=>store.DecideAndApplyAsync(command,new(1,1),1))));
                SqlTest.Require(retries.All(x=>x.ExistingResult && x.Event.EventId==first.Event.EventId),"Retry changed identity");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog")==1,"Concurrent retry added events");
            });
            await Case("same RequestId different command is conflict",()=>Rejected(async()=>
            { await store.DecideAndApplyAsync(Cmd(seat:2,request:command.RequestId),new(1,1),1); },StoreRejection.RequestConflict));
            await Case("same RequestId different owner is conflict",()=>Rejected(async()=>
            { await store.DecideAndApplyAsync(Cmd(owner:"B",request:command.RequestId),new(1,1),1); },StoreRejection.RequestConflict));
            gate.StableIndex=1;
            await Case("NO_CHANGE logs result and keeps seat version",async()=>
            {
                var busy=await store.DecideAndApplyAsync(Cmd(owner:"B"),new(1,1),2);
                SqlTest.Require(BookingEventCodec.Decode(busy.Event).Result.Code=="SEAT_BUSY","Wrong busy result");
                SqlTest.Require(await N(db.ConnectionString,"SELECT VersionIndex FROM dbo.Seats WHERE SeatNo=1")==1,"NO_CHANGE changed seat version");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog")==2,"NO_CHANGE missing log");
            });
            gate.StableIndex=2;
            var reservation=BookingEventCodec.Decode(first.Event).Result.ReservationId;
            Guid? ticket=null;
            await Case("CONFIRM persists ticket and all cross-table relations",async()=>
            {
                var sold=await store.DecideAndApplyAsync(Cmd(BookingAction.Confirm,reservation:reservation),new(1,1),3);
                ticket=BookingEventCodec.Decode(sold.Event).Result.TicketId;
                SqlTest.Require(ticket.HasValue && await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets t JOIN dbo.Reservations r ON r.ReservationId=t.ReservationId AND r.OwnerId=t.OwnerId JOIN dbo.Seats s ON s.SeatNo=t.SeatNo AND s.CurrentReservationId=r.ReservationId WHERE s.Status='SOLD' AND r.Status='CONFIRMED'")==1,"Broken SOLD relations");
            });
            gate.StableIndex=3;
            await Case("new CONFIRM RequestId returns same existing ticket",async()=>
            {
                var repeated=await store.DecideAndApplyAsync(Cmd(BookingAction.Confirm,reservation:reservation),new(1,1),4);
                SqlTest.Require(BookingEventCodec.Decode(repeated.Event).Result==new BusinessResult("ALREADY_CONFIRMED",1,1,reservation,ticket),"Repeated CONFIRM changed ticket");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets")==1,"Created another ticket");
            });
            gate.StableIndex=4;
            await Case("CANCEL SOLD cannot delete ticket",async()=>
            {
                var denied=await store.DecideAndApplyAsync(Cmd(BookingAction.Cancel,reservation:reservation),new(1,1),5);
                SqlTest.Require(BookingEventCodec.Decode(denied.Event).Result.Code=="TICKET_NOT_CANCELLABLE","Cancelled SOLD");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets")==1,"Deleted ticket");
            });
            gate.StableIndex=5;
            var hold2=await store.DecideAndApplyAsync(Cmd(seat:2),new(1,1),6);
            gate.StableIndex=6;
            await Case("CANCEL HELD releases seat and retains reservation history",async()=>
            {
                var res2=BookingEventCodec.Decode(hold2.Event).Result.ReservationId;
                await store.DecideAndApplyAsync(Cmd(BookingAction.Cancel,2,res2),new(1,1),7);
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats WHERE SeatNo=2 AND Status='AVAILABLE' AND CurrentReservationId IS NULL AND CurrentOwnerId IS NULL")==1,"Seat not released");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Reservations WHERE SeatNo=2 AND Status='CANCELLED'")==1,"Reservation history lost");
            });
            gate.StableIndex=7;
            await Case("stable result and reservation reads filter owners",async()=>
            {
                SqlTest.Require(await store.ReadStableResultAsync(command.RequestId,"B",7)==null,"Cross-owner result leak");
                SqlTest.Require((await store.ReadMyReservationsAsync("B",7)).Reservations.Count==0,"Cross-owner reservations leak");
            });
            await Case("seed rerun preserves booked history and authority",async()=>
            {
                await new SqlDatabaseBootstrap(db.ConnectionString).InitializeAsync(Dataset,1);
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Tickets")==1 && await N(db.ConnectionString,"SELECT LastAppliedIndex FROM dbo.NodeState")==7,"Seed reset bookings");
            });
        }

        await using(var db=await TestDatabase.Create(server,2))
        {
            var gate=new TestGate {StableIndex=1}; var store=new SqlBookingStore(new(db.ConnectionString,Dataset,2),gate);
            await store.AppendAndApplyAsync(original,new(1,new(1,1)));
            await Case("backup cannot serve client snapshot",()=>Rejected(async()=>{await store.ReadStableSnapshotAsync(1);},StoreRejection.StaleAuthority));
            await Case("rejects peer claiming another sender",()=>Rejected(async()=>{await store.AppendAndApplyAsync(original,new(2,new(1,1)));},StoreRejection.StaleAuthority));
            await Case("rejects old or wrong authority epoch",()=>Rejected(async()=>{await store.AppendAndApplyAsync(original,new(1,new(2,1)));},StoreRejection.StaleAuthority));
            await Case("rejects modified event hash",()=>Rejected(async()=>{await store.AppendAndApplyAsync(original with{EventHash=new byte[32]},new(1,new(1,1)));},StoreRejection.InvalidEvent));
            await Case("GAP reports exact next index",async()=>
            {
                var gap=BookingEventCodec.Encode(initial with{LogIndex=3});
                try {await store.AppendAndApplyAsync(gap,new(1,new(1,1)));throw new Exception("Accepted GAP");}
                catch(StoreRejectedException e) when(e.Code==StoreRejection.Gap && e.ExpectedIndex==2) { }
            });
            await Case("conflicting index closes gate without overwriting log",async()=>
            {
                var conflict=BookingEventCodec.Encode(initial with{EventId=Guid.NewGuid()});
                await Rejected(async()=>{await store.AppendAndApplyAsync(conflict,new(1,new(1,1)));},StoreRejection.PrefixConflict);
                SqlTest.Require(gate.Blocked && await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog")==1,"Conflict did not fail closed");
            });
        }

        await using(var db=await TestDatabase.Create(server,3,activate:false))
        {
            var gate=new TestGate(); var store=new SqlBookingStore(new(db.ConnectionString,Dataset,3),gate);
            var context=new RecoveryContext(Guid.NewGuid(),Dataset,1,3,2,2,0,Convert.FromHexString(initial.PreviousHash),1,original.EventHash);
            var replaySource=TestPeerIdentityFactory.Create(3,1);
            await Case("replay requires locally installed context",()=>Rejected(async()=>{await store.ApplyReplayEntryAsync(original,replaySource,context);},StoreRejection.NotReady));
            gate.InstalledRecovery=context;
            var wrongReplaySource=TestPeerIdentityFactory.Create(3,2);
            await Case("replay source must match locally installed context",()=>Rejected(async()=>
            {await store.ApplyReplayEntryAsync(original,wrongReplaySource,context);},StoreRejection.NotReady));
            await store.ApplyReplayEntryAsync(original,replaySource,context);
            await Case("replay cannot install authority or copy node identity",async()=>
            {
                SqlTest.Require(await N(db.ConnectionString,"SELECT NodeId FROM dbo.NodeState")==3 && await N(db.ConnectionString,"SELECT AcceptedEpoch FROM dbo.NodeState")==0,"Replay changed local metadata");
            });
            await Case("replay rejects entry beyond pinned K",()=>Rejected(async()=>
            {await store.ApplyReplayEntryAsync(BookingEventCodec.Encode(initial with{LogIndex=2}),replaySource,context);},StoreRejection.IndexMismatch));
            await Case("historical replay does not loosen normal APPEND authority",()=>Rejected(async()=>
            {await store.AppendAndApplyAsync(original,new(1,new(1,1)));},StoreRejection.StaleAuthority));
        }

        foreach(var after in new[]{false,true})
        await using(var db=await TestDatabase.Create(server,1))
        {
            var gate=new TestGate(); var command=Cmd();
            var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture(),new FaultObserver(after));
            await Case(after?"after-commit failure keeps durable event and closes gate":"before-commit failure rolls back every table and closes gate",async()=>
            {
                try {await store.DecideAndApplyAsync(command,new(1,1),1);throw new Exception("Fault did not occur");}
                catch(StorageUncertainException e) when(e.RequestId==command.RequestId) { }
                SqlTest.Require(gate.Blocked,"Storage failure did not close gate");
                var local=await store.InspectLocalRequestAsync(command.RequestId);
                SqlTest.Require(after?local!=null:local==null,"Wrong durable outcome");
                var expected=after?1:0;
                foreach(var table in new[]{"OperationLog","RequestResults","Reservations"})
                    SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo."+table)==expected,"Partial Apply in "+table);
                SqlTest.Require(await N(db.ConnectionString,"SELECT LastAppliedIndex FROM dbo.NodeState")==expected,"Wrong persisted index");
                SqlTest.Require(await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.Seats WHERE Status='HELD'")==expected,"Projection not atomic");
                await Rejected(async()=>{await store.DecideAndApplyAsync(command,new(1,1),1);},StoreRejection.NotReady);
            });
        }
        await using(var db=await TestDatabase.Create(server,1))
        {
            var gate=new TestGate(); using var cancel=new CancellationTokenSource();
            var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture(),new CancelObserver(cancel));
            await Case("cancellation after Apply cannot abandon commit",async()=>
            {
                await store.DecideAndApplyAsync(Cmd(),new(1,1),1,cancel.Token);
                SqlTest.Require(cancel.IsCancellationRequested && await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog")==1,"Admitted operation abandoned");
            });
        }
        await using(var db=await TestDatabase.Create(server,1))
        {
            var gate=new TestGate(); var store=new SqlBookingStore(new(db.ConnectionString,Dataset,1),gate,new HoldFixture());
            var name=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.ConnectionString).InitialCatalog;
            await SqlTest.Execute(db.ConnectionString,$"ALTER DATABASE [{name}] SET DELAYED_DURABILITY=FORCED");
            await Case("forced delayed durability prevents mutation",async()=>
            {
                try{await store.DecideAndApplyAsync(Cmd(),new(1,1),1);throw new Exception("Accepted forced delayed durability");}
                catch(StorageUncertainException) { }
                SqlTest.Require(gate.Blocked && await N(db.ConnectionString,"SELECT COUNT(*) FROM dbo.OperationLog")==0,"Unsafe durable mode wrote data");
            });
        }
        Console.WriteLine($"SQL advanced cases: {count} passed");
    }
    private sealed class FaultObserver(bool after) : IApplyObserver
    {
        public Task BeforeCommitAsync(PersistedEvent entry) { if(!after) throw new IOException("Injected before COMMIT"); return Task.CompletedTask; }
        public Task AfterCommitAsync(PersistedEvent entry) { if(after) throw new IOException("Injected after COMMIT, before reply"); return Task.CompletedTask; }
    }
    private sealed class CancelObserver(CancellationTokenSource source) : IApplyObserver
    {
        public Task BeforeCommitAsync(PersistedEvent entry) {source.Cancel();return Task.CompletedTask;}
        public Task AfterCommitAsync(PersistedEvent entry)=>Task.CompletedTask;
    }
}
