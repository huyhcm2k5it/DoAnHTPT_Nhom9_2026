using De6.Contracts;
using De6.Booking.Persistence;
using Microsoft.Data.SqlClient;

internal sealed class TestGate : IStoreAccessGate
{
    private readonly SemaphoreSlim mutex = new(1);
    public bool Blocked { get; private set; }
    public int Admissions { get; private set; }
    public long StableIndex { get; set; }
    public RecoveryContext? InstalledRecovery { get; set; }
    public IDisposable EnterPrimary(AcceptedAuthority authority, long expectedNextIndex)
        => Enter(() => !Blocked && authority == new AcceptedAuthority(1,1) && expectedNextIndex == StableIndex+1);
    public IDisposable EnterAppend(AuthenticatedPeerAuthority peer) => Enter(() => !Blocked);
    public IDisposable EnterReplay(RecoveryContext context) => Enter(() => InstalledRecovery == context);
    public IDisposable EnterSnapshot(long expectedIndex) => Enter(() => !Blocked && expectedIndex == StableIndex);
    public IDisposable EnterRecoveryInspection() => Enter(() => true);
    public void Block(string reason) { _ = reason; Blocked = true; }
    private IDisposable Enter(Func<bool> allowed)
    {
        Admissions++;
        mutex.Wait();
        if (!allowed()) { mutex.Release(); throw new StoreRejectedException(StoreRejection.NotReady); }
        return new Lease(mutex);
    }
    private sealed class Lease(SemaphoreSlim mutex) : IDisposable { public void Dispose() => mutex.Release(); }
}

// Test-only decision fixture. This is NOT Gia Quân's production decision engine.
internal sealed class HoldFixture : IBookingDecisionEngine
{
    public BookingDecision Decide(CommandIdentity command, BookingState state, long index, DateTime utcNow)
    {
        BookingDecision No(string code,Guid? res=null,Guid? ticket=null) => new(new(DecisionKind.NoChange,null,null,null),new(code,1,state.Seat.SeatNo,res,ticket));
        if(command.Command.Action==BookingAction.Hold)
        {
            if(state.Seat.Status!=SeatStatus.Available) return No(state.Seat.Status==SeatStatus.Held?"SEAT_BUSY":"ALREADY_SOLD");
            var id=Guid.NewGuid();
            return new(new(DecisionKind.HeldCreated,new(1,state.Seat.SeatNo,SeatStatus.Held,id,command.OwnerId,index),
                new(id,1,state.Seat.SeatNo,command.OwnerId,ReservationStatus.Held,utcNow,index),null),new("OK_HELD",1,state.Seat.SeatNo,id,null));
        }
        var r=state.RequestedReservation;
        if(r==null) return No("RESERVATION_NOT_FOUND");
        if(r.OwnerId!=command.OwnerId) return No("NOT_OWNER");
        if(r.SeatNo!=command.Command.SeatNo) return No("RESERVATION_SEAT_MISMATCH");
        if(r.Status==ReservationStatus.Cancelled) return command.Command.Action==BookingAction.Cancel?No("ALREADY_CANCELLED",r.ReservationId):No("RESERVATION_CANCELLED");
        if(r.Status==ReservationStatus.Confirmed) return command.Command.Action==BookingAction.Cancel?No("TICKET_NOT_CANCELLABLE"):No("ALREADY_CONFIRMED",r.ReservationId,state.ExistingTicket!.TicketId);
        if(command.Command.Action==BookingAction.Cancel)
            return new(new(DecisionKind.HoldCancelled,new(1,r.SeatNo,SeatStatus.Available,null,null,index),
                r with{Status=ReservationStatus.Cancelled,UpdatedIndex=index},null),new("OK_CANCELLED",1,r.SeatNo,r.ReservationId,null));
        var ticketId=Guid.NewGuid();
        return new(new(DecisionKind.SoldConfirmed,new(1,r.SeatNo,SeatStatus.Sold,r.ReservationId,r.OwnerId,index),
            r with{Status=ReservationStatus.Confirmed,UpdatedIndex=index},new(ticketId,r.ReservationId,1,r.SeatNo,r.OwnerId,utcNow,index)),
            new("OK_SOLD",1,r.SeatNo,r.ReservationId,ticketId));
    }
}

internal static class SqlTest
{
    internal static async Task<object?> Scalar(string connectionString, string query)
    {
        await using var connection=new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command=new SqlCommand(query,connection);
        return await command.ExecuteScalarAsync();
    }
    internal static async Task Execute(string connectionString, string query)
    {
        await using var connection=new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command=new SqlCommand(query,connection);
        await command.ExecuteNonQueryAsync();
    }
    internal static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
}
