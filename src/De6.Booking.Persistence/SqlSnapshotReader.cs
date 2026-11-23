using De6.Contracts;
using System.Data;
using Microsoft.Data.SqlClient;
namespace De6.Booking.Persistence;
public sealed partial class SqlBookingStore
{
    public async Task<StableSnapshot> ReadStableSnapshotAsync(long expectedIndex,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=gate.EnterSnapshot(expectedIndex);
        return await Transaction(async(connection,transaction,node)=>
        {
            RequireSnapshot(node,expectedIndex);
            using var command=SqlSupport.Command(connection,transaction,
                "SELECT TripId,SeatNo,Status,CurrentReservationId,CurrentOwnerId,VersionIndex FROM dbo.Seats ORDER BY TripId,SeatNo");
            await using var reader=await command.ExecuteReaderAsync();
            var seats=new List<SeatView>();
            while(await reader.ReadAsync())
                seats.Add(new(reader.GetInt32(0),reader.GetInt32(1),Enum.Parse<SeatStatus>(reader.GetString(2),true),
                    reader.IsDBNull(3)?null:reader.GetGuid(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetInt64(5)));
            Require(seats.Count==10,StoreRejection.InvariantViolation);
            return (new StableSnapshot(options.DatasetId,node.Epoch,node.Index,seats),(PersistedEvent?)null);
        },null,cancellation);
    }

    public async Task<StoredResultSnapshot?> ReadStableResultAsync(Guid requestId,string owner,long expectedIndex,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        BookingCanonical.ValidateOwner(owner);
        if(requestId==Guid.Empty) throw new ArgumentException("RequestId required");
        using var lease=gate.EnterSnapshot(expectedIndex);
        return await Transaction(async(connection,transaction,node)=>
        {
            RequireSnapshot(node,expectedIndex);
            using var command=SqlSupport.Command(connection,transaction,
                "SELECT LogIndex,ResultPayloadBytes FROM dbo.RequestResults WHERE RequestId=@id AND OwnerId=@owner",
                ("@id",requestId),("@owner",owner));
            await using var reader=await command.ExecuteReaderAsync();
            StoredResultSnapshot? result=null;
            if(await reader.ReadAsync())
            {
                Require(reader.GetInt64(0)<=node.Index,StoreRejection.InvariantViolation);
                result=new(node.Index,requestId,owner,reader.GetInt64(0),(byte[])reader[1]);
            }
            return (result,(PersistedEvent?)null);
        },requestId,cancellation);
    }

    public async Task<ReservationsSnapshot> ReadMyReservationsAsync(string owner,long expectedIndex,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        BookingCanonical.ValidateOwner(owner);
        using var lease=gate.EnterSnapshot(expectedIndex);
        return await Transaction(async(connection,transaction,node)=>
        {
            RequireSnapshot(node,expectedIndex);
            using var command=SqlSupport.Command(connection,transaction,
                "SELECT TripId,SeatNo,OwnerId,Status,CreatedUtc,UpdatedIndex,ReservationId FROM dbo.Reservations WHERE OwnerId=@owner ORDER BY ReservationId",("@owner",owner));
            await using var reader=await command.ExecuteReaderAsync();
            var rows=new List<ReservationData>();
            while(await reader.ReadAsync()) rows.Add(ReservationFrom(reader,reader.GetGuid(6)));
            return (new ReservationsSnapshot(options.DatasetId,node.Epoch,node.Index,rows),(PersistedEvent?)null);
        },null,cancellation);
    }

    /// <summary>Server-side lookup at the authoritative prefix for request identity and deduplication.</summary>
    public async Task<PersistedEvent?> FindStableRequestAsync(Guid requestId, long expectedIndex, CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=gate.EnterSnapshot(expectedIndex);
        return await Transaction(async(connection,transaction,node)=>
        {
            RequireSnapshot(node,expectedIndex);
            var entry=await FindEvent(connection,transaction,"l.RequestId=@id",requestId);
            Require(entry is null || entry.LogIndex<=node.Index,StoreRejection.InvariantViolation);
            return (entry,(PersistedEvent?)null);
        },requestId,cancellation);
    }

    /// <summary>Administrative LOCAL evidence only: existence does not imply Stable or FINAL.</summary>
    public async Task<StoreApplyResult?> InspectLocalRequestAsync(Guid requestId,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(requestId==Guid.Empty) throw new ArgumentException("RequestId required");
        using var lease=gate.EnterRecoveryInspection();
        return await Transaction(async(connection,transaction,node)=>
        {
            _=node;
            var entry=await FindEvent(connection,transaction,"l.RequestId=@id",requestId);
            return (entry==null?null:new StoreApplyResult(entry,true),(PersistedEvent?)null);
        },requestId,cancellation);
    }

    private void RequireSnapshot(Node node,long expected)
    {
        Require(node.Epoch>0 && node.Leader==options.NodeId,StoreRejection.StaleAuthority);
        Require(node.Index==expected,StoreRejection.IndexMismatch,node.Index);
    }
}
