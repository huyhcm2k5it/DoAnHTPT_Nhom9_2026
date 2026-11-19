using De6.Contracts;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
namespace De6.Booking.Persistence;
public sealed partial class SqlBookingStore
{
    public async Task<VerifiedRecoveryHistory> GetVerifiedRecoveryHistoryAsync(long expectedIndex,
        CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease=gate.EnterMaintenance(expectedIndex);
        return await Transaction(async(c,t,node)=>
        {
            Require(node.Index==expectedIndex,StoreRejection.IndexMismatch,node.Index);
            var audit=await Audit(c,t,node);
            Require(audit.MatchesHistory,StoreRejection.InvariantViolation);
            var history=await History(c,t,node);
            AcceptedAuthority? authority;
            if(node.Epoch==0 && node.Leader is null) authority=null;
            else if(node.Epoch>0 && node.Leader is >=1 and <=3) authority=new AcceptedAuthority(node.Epoch,node.Leader.Value);
            else throw new StoreRejectedException(StoreRejection.InvariantViolation);

            var proof=new VerifiedRecoveryHistory(options.NodeId,options.DatasetId,node.SchemaVersion,authority,node.Index,
                node.Hash,audit.BusinessDigest,audit.MatchesHistory,true,history.Checkpoints);
            return (proof,(PersistedEvent?)null);
        },null,cancellationToken);
    }

    public async Task<LocalStorageAudit> AuditLocalAsync(CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=gate.EnterRecoveryInspection();
        return await Transaction(async(c,t,node)=>
        {
            var audit=await Audit(c,t,node);
            if(!audit.MatchesHistory) gate.Block("INVARIANT_VIOLATION");
            return (audit,(PersistedEvent?)null);
        },null,cancellation);
    }
    public async Task<FrozenPrefixReport> GetFrozenPrefixAsync(long expectedIndex,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=gate.EnterMaintenance(expectedIndex);
        return await Transaction(async(c,t,node)=>
        {
            Require(node.Index==expectedIndex,StoreRejection.IndexMismatch,node.Index);
            var audit=await Audit(c,t,node);
            Require(audit.MatchesHistory,StoreRejection.InvariantViolation);
            return (new FrozenPrefixReport(options.NodeId,options.DatasetId,node.SchemaVersion,node.Index,node.Hash.ToArray(),audit.BusinessDigest.ToArray()),(PersistedEvent?)null);
        },null,cancellation);
    }

    public async Task<FrozenPrefixReport> RebuildProjectionAsync(long expectedIndex,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=gate.EnterMaintenance(expectedIndex);
        return await Transaction(async(c,t,node)=>
        {
            Require(node.Index==expectedIndex,StoreRejection.IndexMismatch,node.Index);
            var history=await History(c,t,node); // Validate every immutable event BEFORE changing derived rows.
            await SqlSupport.Execute(c,t,
                "DELETE dbo.Tickets; UPDATE dbo.Seats SET Status='AVAILABLE',CurrentReservationId=NULL,CurrentOwnerId=NULL,VersionIndex=0; DELETE dbo.Reservations; DELETE dbo.RequestResults; DELETE dbo.Seats; DELETE dbo.Trips; INSERT dbo.Trips(TripId,TotalSeats) VALUES(1,10); INSERT dbo.Seats(TripId,SeatNo,Status,VersionIndex) SELECT 1,v.n,'AVAILABLE',0 FROM(VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) v(n); UPDATE dbo.NodeState SET LastAppliedIndex=0,HeadHash=@seed WHERE RowId=1;",
                CancellationToken.None,("@seed",SeedHash()));
            var cursor=node with{Index=0,Hash=SeedHash()};
            foreach(var entry in history.Entries)
            {
                var value=BookingEventCodec.Decode(entry);
                var state=await LoadState(c,t,value.CommandData);
                ValidateDecision(value,state);
                await Apply(c,t,cursor,value,entry,state,appendLog:false);
                cursor=cursor with{Index=entry.LogIndex,Hash=entry.EventHash.ToArray()};
            }
            var audit=await Audit(c,t,node);
            Require(audit.MatchesHistory,StoreRejection.InvariantViolation);
            var report=new FrozenPrefixReport(options.NodeId,options.DatasetId,node.SchemaVersion,node.Index,node.Hash.ToArray(),audit.BusinessDigest.ToArray());
            return (report,history.Entries.LastOrDefault());
        },null,cancellation);
    }

    private byte[] SeedHash()=>SHA256.HashData(Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|"+options.DatasetId.ToString("N")));

    private sealed class HistoryProjection
    {
        internal SeatView[] Seats=Enumerable.Range(1,10).Select(n=>new SeatView(1,n,SeatStatus.Available,null,null,0)).ToArray();
        internal Dictionary<Guid,ReservationData> Reservations=new();
        internal Dictionary<Guid,TicketData> Tickets=new(); // keyed by reservation
        internal Dictionary<Guid,DurableResultView> Results=new();
        private readonly HashSet<Guid> eventIds=new(),ticketIds=new();
        private long epoch; private int writer;
        internal StorageProjection Material()=>new(Seats,Reservations.Values.ToArray(),Tickets.Values.ToArray(),Results.Values.ToArray());
        internal void Apply(BookingEvent e,PersistedEvent entry)
        {
            Require(e.Epoch>=epoch && (e.Epoch!=epoch || e.WriterId==writer) && eventIds.Add(e.EventId) && !Results.ContainsKey(e.RequestId),StoreRejection.PrefixConflict);
            epoch=e.Epoch;writer=e.WriterId;
            var seat=Seats[e.CommandData.SeatNo-1];
            ReservationData? requested=null,current=null;TicketData? requestedTicket=null,currentTicket=null;
            if(e.CommandData.ReservationId.HasValue) {Reservations.TryGetValue(e.CommandData.ReservationId.Value,out requested);Tickets.TryGetValue(e.CommandData.ReservationId.Value,out requestedTicket);}
            if(seat.ReservationId.HasValue) {Reservations.TryGetValue(seat.ReservationId.Value,out current);Tickets.TryGetValue(seat.ReservationId.Value,out currentTicket);}
            ValidateDecision(e,new(seat,requested,current,requestedTicket,currentTicket));
            if(e.Decision.Kind!=DecisionKind.NoChange)
            {
                var r=e.Decision.ReservationAfter!;
                if(e.Decision.Kind==DecisionKind.HeldCreated) Require(!Reservations.ContainsKey(r.ReservationId),StoreRejection.PrefixConflict);
                Reservations[r.ReservationId]=r;Seats[e.CommandData.SeatNo-1]=e.Decision.SeatAfter!;
                if(e.Decision.TicketCreated is {} ticket)
                {
                    Require(ticketIds.Add(ticket.TicketId) && Tickets.TryAdd(ticket.ReservationId,ticket),StoreRejection.PrefixConflict);
                }
            }
            Results.Add(e.RequestId,new(e.RequestId,e.OwnerId,e.CommandHash,e.LogIndex,entry.ResultPayload.ToArray()));
        }
    }

    private sealed record VerifiedHistory(List<PersistedEvent> Entries,HistoryProjection Projection,
        IReadOnlyList<RecoveryHistoryCheckpoint> Checkpoints);

    private async Task<VerifiedHistory> History(SqlConnection c,SqlTransaction t,Node node)
    {
        var entries=new List<PersistedEvent>();var model=new HistoryProjection();var hash=SeedHash();long index=0;
        var checkpoints=new List<RecoveryHistoryCheckpoint>
        {
            new(0,ReadOnlyMemory<byte>.Empty,hash,ProjectionDigest.Compute(options.DatasetId,0,model.Material()))
        };
        using var command=SqlSupport.Command(c,t,"SELECT LogIndex,Epoch,WriterId,EventId,RequestId,PreviousHash,EventHash,PayloadBytes FROM dbo.OperationLog ORDER BY LogIndex");
        await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            try
            {
                var payload=(byte[])reader[7];var e=BookingEventCodec.Read(payload);
                var entry=new PersistedEvent(options.DatasetId,reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetGuid(3),payload,(byte[])reader[6],BookingCanonical.ResultBytes(e.Result));
                BookingEventCodec.Decode(entry);
                Require(e.RequestId==reader.GetGuid(4) && entry.LogIndex==++index &&
                    e.PreviousHash==Convert.ToHexStringLower(hash) && ((byte[])reader[5]).AsSpan().SequenceEqual(hash),StoreRejection.PrefixConflict);
                model.Apply(e,entry);hash=entry.EventHash.ToArray();entries.Add(entry);
                checkpoints.Add(new RecoveryHistoryCheckpoint(index,Convert.FromHexString(e.PreviousHash),hash,
                    ProjectionDigest.Compute(options.DatasetId,index,model.Material())));
            }
            catch(Exception e) when(e is ArgumentException or System.Text.Json.JsonException or FormatException or StoreRejectedException)
            {throw new StoreRejectedException(StoreRejection.PrefixConflict);}
        }
        Require(index==node.Index && hash.AsSpan().SequenceEqual(node.Hash),StoreRejection.PrefixConflict);
        return new VerifiedHistory(entries,model,checkpoints);
    }

    private static async Task<StorageProjection> Projection(SqlConnection c,SqlTransaction t)
    {
        var seats=new List<SeatView>();var reservations=new List<ReservationData>();var tickets=new List<TicketData>();var results=new List<DurableResultView>();
        using(var query=SqlSupport.Command(c,t,"SELECT TripId,SeatNo,Status,CurrentReservationId,CurrentOwnerId,VersionIndex FROM dbo.Seats"))
        await using(var r=await query.ExecuteReaderAsync())
            while(await r.ReadAsync()) seats.Add(new(r.GetInt32(0),r.GetInt32(1),Enum.Parse<SeatStatus>(r.GetString(2),true),r.IsDBNull(3)?null:r.GetGuid(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt64(5)));
        using(var query=SqlSupport.Command(c,t,"SELECT TripId,SeatNo,OwnerId,Status,CreatedUtc,UpdatedIndex,ReservationId FROM dbo.Reservations"))
        await using(var r=await query.ExecuteReaderAsync()) while(await r.ReadAsync()) reservations.Add(ReservationFrom(r,r.GetGuid(6)));
        using(var query=SqlSupport.Command(c,t,"SELECT TicketId,ReservationId,TripId,SeatNo,OwnerId,SoldUtc,CreatedIndex FROM dbo.Tickets"))
        await using(var r=await query.ExecuteReaderAsync()) while(await r.ReadAsync()) tickets.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),DateTime.SpecifyKind(r.GetDateTime(5),DateTimeKind.Utc),r.GetInt64(6)));
        using(var query=SqlSupport.Command(c,t,"SELECT RequestId,OwnerId,CommandHash,LogIndex,ResultPayloadBytes FROM dbo.RequestResults"))
        await using(var r=await query.ExecuteReaderAsync()) while(await r.ReadAsync()) results.Add(new(r.GetGuid(0),r.GetString(1),Convert.ToHexStringLower((byte[])r[2]),r.GetInt64(3),(byte[])r[4]));
        return new(seats,reservations,tickets,results);
    }

    private async Task<LocalStorageAudit> Audit(SqlConnection c,SqlTransaction t,Node node)
    {
        var history=await History(c,t,node);
        var expected=ProjectionDigest.Compute(options.DatasetId,node.Index,history.Projection.Material());
        var actual=ProjectionDigest.Compute(options.DatasetId,node.Index,await Projection(c,t));
        var violations=new List<OracleViolation>();
        using var resource=typeof(SqlBookingStore).Assembly.GetManifestResourceStream("De6.Oracle.001.sql") ?? throw new InvalidOperationException("Missing SQL oracle");
        using var text=new StreamReader(resource);
        using var query=SqlSupport.Command(c,t,await text.ReadToEndAsync(),("@seedHash",SeedHash()));
        await using var reader=await query.ExecuteReaderAsync();
        while(await reader.ReadAsync()) if(Convert.ToInt64(reader.GetValue(1))>0) violations.Add(new(reader.GetString(0),Convert.ToInt64(reader.GetValue(1))));
        if(!actual.AsSpan().SequenceEqual(expected)) violations.Add(new("PROJECTION_DIGEST",1));
        return new(options.NodeId,options.DatasetId,node.Index,node.Hash.ToArray(),actual,violations.Count==0,violations);
    }
}
