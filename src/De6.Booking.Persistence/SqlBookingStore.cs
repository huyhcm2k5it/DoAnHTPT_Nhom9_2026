using System.Data;
using De6.Contracts;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

public sealed partial class SqlBookingStore : IBookingStore, IRecoveryEvidenceStore, IRecoveryManifestStore
{
    private readonly SqlStoreOptions options;
    private readonly IStoreAccessGate gate;
    private readonly IBookingDecisionEngine? engine;
    private readonly IApplyObserver? observer;

    public SqlBookingStore(SqlStoreOptions options, IStoreAccessGate gate, IBookingDecisionEngine? engine = null, IApplyObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(gate);
        if(options.DatasetId==Guid.Empty || options.NodeId is <1 or >3) throw new ArgumentException("Invalid store identity");
        using var validate=SqlSupport.Connection(options.ConnectionString);
        this.options=options; this.gate=gate; this.engine=engine; this.observer=observer;
    }

    public async Task<StoreApplyResult> DecideAndApplyAsync(CommandIdentity command, AcceptedAuthority authority,
        long expectedNextIndex, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ValidateCommand(command);
        using var lease=gate.EnterPrimary(authority,expectedNextIndex);
        return await Transaction(async (connection,transaction,node) =>
        {
            Require(authority.Epoch==node.Epoch && authority.LeaderId==node.Leader && options.NodeId==node.Leader,
                StoreRejection.StaleAuthority);
            var existing=await FindEvent(connection,transaction,"l.RequestId=@id",command.RequestId);
            if(existing!=null)
            {
                var old=BookingEventCodec.Decode(existing);
                Require(old.OwnerId==command.OwnerId && old.CommandHash==command.CommandHash,StoreRejection.RequestConflict);
                return (new StoreApplyResult(existing,true),(PersistedEvent?)null);
            }
            Require(expectedNextIndex==node.Index+1,StoreRejection.IndexMismatch,node.Index+1);
            var state=await LoadState(connection,transaction,command.Command);
            var decision=(engine ?? throw new InvalidOperationException("Primary requires a decision engine"))
                .Decide(command,state,expectedNextIndex,DateTime.UtcNow);
            var value=new BookingEvent(1,options.DatasetId,expectedNextIndex,authority.Epoch,authority.LeaderId,
                Guid.NewGuid(),Convert.ToHexStringLower(node.Hash),command.RequestId,command.OwnerId,
                command.CommandHash,command.Command,state.Seat.VersionIndex,decision.Patch,decision.Result);
            var entry=BookingEventCodec.Encode(value);
            ValidateDecision(value,state);
            await Apply(connection,transaction,node,value,entry,state);
            return (new StoreApplyResult(entry,false),(PersistedEvent?)entry);
        },command.RequestId,cancellation);
    }

    private void ValidateCommand(CommandIdentity command)
    {
        Require(command.DatasetId==options.DatasetId,StoreRejection.DatasetMismatch);
        if(command.RequestId==Guid.Empty || command.CommandHash!=BookingCanonical.CommandHash(command.DatasetId,command.OwnerId,command.Command))
            throw new ArgumentException("Invalid command identity/hash");
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,StoreRejection code,long? expected=null)
    { if(!condition) throw new StoreRejectedException(code,expected); }

    private sealed record Node(long Epoch,int? Leader,long Index,byte[] Hash,int SchemaVersion);

    // The caller holds a trusted gate lease; no network operation occurs in this SQL transaction.
    // Cancellation is honored before starting; admitted Apply is not abandoned after socket cancellation.
    private async Task<T> Transaction<T>(Func<SqlConnection,SqlTransaction,Node,Task<(T Value,PersistedEvent? Entry)>> work,
        Guid? requestId,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var preparing=true;
        try
        {
            await using var connection=SqlSupport.Connection(options.ConnectionString);
            await connection.OpenAsync(cancellation);
            await SqlSupport.RequireDurable(connection,cancellation);
            await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellation);
            cancellation.ThrowIfCancellationRequested();
            preparing=false;
            var node=await ReadNode(connection,transaction);
            var result=await work(connection,transaction,node);
            if(result.Entry!=null && observer!=null) await observer.BeforeCommitAsync(result.Entry);
            await transaction.CommitAsync(CancellationToken.None);
            if(result.Entry!=null)
            {
                if(observer!=null) await observer.AfterCommitAsync(result.Entry);
                if(gate is ILocalApplyProgress progress) progress.RecordLocalAppliedIndex(result.Entry.LogIndex);
            }
            return result.Value;
        }
        catch(OperationCanceledException) when(preparing && cancellation.IsCancellationRequested)
        {
            // No mutation was admitted to SQL work; disposal rolls back any empty transaction.
            throw;
        }
        catch(StoreRejectedException e)
        {
            if(e.Code is StoreRejection.PrefixConflict or StoreRejection.InvariantViolation) gate.Block(e.Code.ToString());
            throw;
        }
        catch(Exception e)
        {
            gate.Block("STORAGE_FAULT");
            throw new StorageUncertainException(requestId,e);
        }
    }

    private async Task<Node> ReadNode(SqlConnection connection,SqlTransaction transaction)
    {
        using var command=SqlSupport.Command(connection,transaction,
            "SELECT NodeId,DatasetId,AcceptedEpoch,AcceptedLeader,LastAppliedIndex,HeadHash,SchemaVersion FROM dbo.NodeState WITH(UPDLOCK,HOLDLOCK) WHERE RowId=1");
        await using var reader=await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(),StoreRejection.NotReady);
        Require(reader.GetInt32(0)==options.NodeId && reader.GetGuid(1)==options.DatasetId,StoreRejection.DatasetMismatch);
        Require(reader.GetInt32(6)==2,StoreRejection.NotReady);
        return new(reader.GetInt64(2),reader.IsDBNull(3)?null:reader.GetInt32(3),reader.GetInt64(4),(byte[])reader[5],reader.GetInt32(6));
    }

    private async Task<PersistedEvent?> FindEvent(SqlConnection connection,SqlTransaction transaction,string predicate,object id)
    {
        using var command=SqlSupport.Command(connection,transaction,
            "SELECT l.LogIndex,l.Epoch,l.WriterId,l.EventId,l.PreviousHash,l.EventHash,l.PayloadBytes,r.ResultPayloadBytes FROM dbo.OperationLog l JOIN dbo.RequestResults r ON r.LogIndex=l.LogIndex AND r.RequestId=l.RequestId WHERE "+predicate,
            ("@id",id));
        await using var reader=await command.ExecuteReaderAsync();
        if(!await reader.ReadAsync()) return null;
        var entry=new PersistedEvent(options.DatasetId,reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetGuid(3),
            (byte[])reader[6],(byte[])reader[5],(byte[])reader[7]);
        var value=BookingEventCodec.Decode(entry);
        Require(value.PreviousHash==Convert.ToHexStringLower((byte[])reader[4]),StoreRejection.PrefixConflict);
        return entry;
    }

    private static async Task<BookingState> LoadState(SqlConnection connection,SqlTransaction transaction,CommandData command)
    {
        SeatView seat;
        using(var query=SqlSupport.Command(connection,transaction,
            "SELECT Status,CurrentReservationId,CurrentOwnerId,VersionIndex FROM dbo.Seats WHERE TripId=@trip AND SeatNo=@seat",
            ("@trip",command.TripId),("@seat",command.SeatNo)))
        await using(var reader=await query.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync(),StoreRejection.InvariantViolation);
            seat=new(command.TripId,command.SeatNo,Enum.Parse<SeatStatus>(reader.GetString(0),true),
                reader.IsDBNull(1)?null:reader.GetGuid(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetInt64(3));
        }
        var requested=command.ReservationId.HasValue ? await ReadReservation(connection,transaction,command.ReservationId.Value) : null;
        var current=seat.ReservationId.HasValue ? await ReadReservation(connection,transaction,seat.ReservationId.Value) : null;
        var ticket=requested!=null ? await ReadTicket(connection,transaction,requested.ReservationId) : null;
        var currentTicket=current!=null ? await ReadTicket(connection,transaction,current.ReservationId) : null;
        return new(seat,requested,current,ticket,currentTicket);
    }

    private static async Task<ReservationData?> ReadReservation(SqlConnection connection,SqlTransaction transaction,Guid id)
    {
        using var command=SqlSupport.Command(connection,transaction,
            "SELECT TripId,SeatNo,OwnerId,Status,CreatedUtc,UpdatedIndex FROM dbo.Reservations WHERE ReservationId=@id",("@id",id));
        await using var reader=await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReservationFrom(reader,id) : null;
    }

    private static ReservationData ReservationFrom(SqlDataReader reader,Guid id) => new(id,reader.GetInt32(0),reader.GetInt32(1),
        reader.GetString(2),Enum.Parse<ReservationStatus>(reader.GetString(3),true),DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc),reader.GetInt64(5));

    private static async Task<TicketData?> ReadTicket(SqlConnection connection,SqlTransaction transaction,Guid reservation)
    {
        using var command=SqlSupport.Command(connection,transaction,
            "SELECT TicketId,TripId,SeatNo,OwnerId,SoldUtc,CreatedIndex FROM dbo.Tickets WHERE ReservationId=@id",("@id",reservation));
        await using var reader=await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.GetGuid(0),reservation,reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3),
            DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc),reader.GetInt64(5)) : null;
    }
}
