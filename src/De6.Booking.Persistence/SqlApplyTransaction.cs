using De6.Contracts;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

public sealed partial class SqlBookingStore
{
    private static void ValidateDecision(BookingEvent value,BookingState state)
    {
        var p=value.Decision; var s=state.Seat; var r=p.ReservationAfter; var after=p.SeatAfter;
        var command=value.CommandData;
        Require(value.BeforeSeatVersion==s.VersionIndex && s.VersionIndex<value.LogIndex,StoreRejection.InvariantViolation);
        if(s.Status==SeatStatus.Available)
            Require(state.CurrentReservation==null && state.CurrentTicket==null && s.ReservationId==null && s.OwnerId==null,StoreRejection.InvariantViolation);
        else
        {
            var current=state.CurrentReservation;
            Require(current!=null && current.ReservationId==s.ReservationId && current.OwnerId==s.OwnerId &&
                current.TripId==s.TripId && current.SeatNo==s.SeatNo &&
                current.Status==(s.Status==SeatStatus.Held?ReservationStatus.Held:ReservationStatus.Confirmed),StoreRejection.InvariantViolation);
            Require(s.Status==SeatStatus.Sold ? state.CurrentTicket!=null && state.CurrentTicket.ReservationId==s.ReservationId &&
                state.CurrentTicket.SeatNo==s.SeatNo && state.CurrentTicket.OwnerId==s.OwnerId : state.CurrentTicket==null,StoreRejection.InvariantViolation);
        }
        if(p.Kind==DecisionKind.NoChange)
        {
            Require(after==null && r==null && p.TicketCreated==null,StoreRejection.InvariantViolation);
            Require(ExpectedNoChange(value,state)==value.Result,StoreRejection.InvariantViolation);
            return;
        }
        Require(after!=null && r!=null && after.TripId==command.TripId && after.SeatNo==command.SeatNo &&
            after.VersionIndex==value.LogIndex && r.TripId==command.TripId && r.SeatNo==command.SeatNo &&
            r.OwnerId==value.OwnerId && r.ReservationId!=Guid.Empty && r.UpdatedIndex==value.LogIndex && r.CreatedUtc.Kind==DateTimeKind.Utc,
            StoreRejection.InvariantViolation);
        if(p.Kind==DecisionKind.HeldCreated)
        {
            Require(command.Action==BookingAction.Hold && s.Status==SeatStatus.Available && r.Status==ReservationStatus.Held &&
                after.Status==SeatStatus.Held && after.ReservationId==r.ReservationId && after.OwnerId==r.OwnerId && p.TicketCreated==null &&
                value.Result==new BusinessResult("OK_HELD",1,s.SeatNo,r.ReservationId,null),StoreRejection.InvariantViolation);
            return;
        }
        var old=state.RequestedReservation;
        Require(old!=null && old.ReservationId==command.ReservationId && old.ReservationId==s.ReservationId &&
            old.OwnerId==value.OwnerId && old.TripId==command.TripId && old.SeatNo==command.SeatNo &&
            old.Status==ReservationStatus.Held && s.Status==SeatStatus.Held &&
            r.ReservationId==old.ReservationId && r.CreatedUtc==old.CreatedUtc,StoreRejection.InvariantViolation);
        if(p.Kind==DecisionKind.SoldConfirmed)
        {
            var ticket=p.TicketCreated;
            Require(command.Action==BookingAction.Confirm && r.Status==ReservationStatus.Confirmed && after.Status==SeatStatus.Sold &&
                after.ReservationId==r.ReservationId && after.OwnerId==r.OwnerId && ticket!=null &&
                ticket.TicketId!=Guid.Empty && ticket.ReservationId==r.ReservationId && ticket.TripId==1 &&
                ticket.SeatNo==s.SeatNo && ticket.OwnerId==value.OwnerId && ticket.CreatedIndex==value.LogIndex &&
                ticket.SoldUtc.Kind==DateTimeKind.Utc && value.Result==new BusinessResult("OK_SOLD",1,s.SeatNo,r.ReservationId,ticket.TicketId),
                StoreRejection.InvariantViolation);
        }
        else
            Require(p.Kind==DecisionKind.HoldCancelled && command.Action==BookingAction.Cancel && r.Status==ReservationStatus.Cancelled &&
                after.Status==SeatStatus.Available && after.ReservationId==null && after.OwnerId==null && p.TicketCreated==null &&
                value.Result==new BusinessResult("OK_CANCELLED",1,s.SeatNo,r.ReservationId,null),StoreRejection.InvariantViolation);
    }

    private static BusinessResult? ExpectedNoChange(BookingEvent e,BookingState state)
    {
        string? code; Guid? reservation=null; Guid? ticket=null;
        if(e.CommandData.Action==BookingAction.Hold)
            code=state.Seat.Status switch { SeatStatus.Held=>"SEAT_BUSY",SeatStatus.Sold=>"ALREADY_SOLD",_=>null };
        else
        {
            var r=state.RequestedReservation;
            if(r==null) code="RESERVATION_NOT_FOUND";
            else if(r.OwnerId!=e.OwnerId) code="NOT_OWNER";
            else if(r.SeatNo!=e.CommandData.SeatNo || r.TripId!=e.CommandData.TripId) code="RESERVATION_SEAT_MISMATCH";
            else if(r.Status==ReservationStatus.Confirmed)
            {
                if(e.CommandData.Action==BookingAction.Cancel) code="TICKET_NOT_CANCELLABLE";
                else
                {
                    Require(state.ExistingTicket!=null,StoreRejection.InvariantViolation);
                    code="ALREADY_CONFIRMED"; reservation=r.ReservationId; ticket=state.ExistingTicket.TicketId;
                }
            }
            else if(r.Status==ReservationStatus.Cancelled)
            {
                code=e.CommandData.Action==BookingAction.Cancel?"ALREADY_CANCELLED":"RESERVATION_CANCELLED";
                if(code=="ALREADY_CANCELLED") reservation=r.ReservationId;
            }
            else code=null;
        }
        return code==null ? null : new(code,1,e.CommandData.SeatNo,reservation,ticket);
    }

    private async Task Apply(SqlConnection connection,SqlTransaction transaction,Node node,BookingEvent value,PersistedEvent entry,BookingState state)
    {
        if(value.Decision.Kind!=DecisionKind.NoChange)
        {
            var r=value.Decision.ReservationAfter!;
            var args=new (string,object?)[] {("@id",r.ReservationId),("@trip",r.TripId),("@seat",r.SeatNo),("@owner",r.OwnerId),
                ("@status",r.Status.ToString().ToUpperInvariant()),("@utc",r.CreatedUtc),("@idx",r.UpdatedIndex)};
            if(value.Decision.Kind==DecisionKind.HeldCreated)
                await SqlSupport.Execute(connection,transaction,
                    "INSERT dbo.Reservations(ReservationId,TripId,SeatNo,OwnerId,Status,CreatedUtc,UpdatedIndex) VALUES(@id,@trip,@seat,@owner,@status,@utc,@idx)",CancellationToken.None,args);
            else
            {
                var rows=await SqlSupport.Execute(connection,transaction,
                    "UPDATE dbo.Reservations SET Status=@status,UpdatedIndex=@idx WHERE ReservationId=@id AND Status='HELD'",CancellationToken.None,args);
                Require(rows==1,StoreRejection.InvariantViolation);
            }
            var seat=value.Decision.SeatAfter!;
            Require(await SqlSupport.Execute(connection,transaction,
                "UPDATE dbo.Seats SET Status=@status,CurrentReservationId=@res,CurrentOwnerId=@owner,VersionIndex=@idx WHERE TripId=@trip AND SeatNo=@seat AND VersionIndex=@before",
                CancellationToken.None,("@status",seat.Status.ToString().ToUpperInvariant()),("@res",seat.ReservationId),("@owner",seat.OwnerId),
                ("@idx",seat.VersionIndex),("@trip",seat.TripId),("@seat",seat.SeatNo),("@before",state.Seat.VersionIndex))==1,StoreRejection.InvariantViolation);
            var ticket=value.Decision.TicketCreated;
            if(ticket!=null)
                await SqlSupport.Execute(connection,transaction,
                    "INSERT dbo.Tickets(TicketId,ReservationId,TripId,SeatNo,OwnerId,SoldUtc,CreatedIndex) VALUES(@id,@res,@trip,@seat,@owner,@utc,@idx)",
                    CancellationToken.None,("@id",ticket.TicketId),("@res",ticket.ReservationId),("@trip",ticket.TripId),
                    ("@seat",ticket.SeatNo),("@owner",ticket.OwnerId),("@utc",ticket.SoldUtc),("@idx",ticket.CreatedIndex));
        }
        await SqlSupport.Execute(connection,transaction,
            "INSERT dbo.OperationLog(LogIndex,EventId,Epoch,WriterId,RequestId,PreviousHash,EventHash,PayloadBytes) VALUES(@idx,@event,@epoch,@writer,@request,@prev,@hash,@payload); INSERT dbo.RequestResults(RequestId,OwnerId,CommandHash,LogIndex,ResultPayloadBytes) VALUES(@request,@owner,@commandHash,@idx,@result)",
            CancellationToken.None,("@idx",entry.LogIndex),("@event",entry.EventId),("@epoch",entry.Epoch),("@writer",entry.WriterId),
            ("@request",value.RequestId),("@prev",Convert.FromHexString(value.PreviousHash)),("@hash",entry.EventHash.ToArray()),
            ("@payload",entry.PayloadBytes.ToArray()),("@owner",value.OwnerId),("@commandHash",Convert.FromHexString(value.CommandHash)),("@result",entry.ResultPayload.ToArray()));
        Require(await SqlSupport.Execute(connection,transaction,
            "UPDATE dbo.NodeState SET LastAppliedIndex=@idx,HeadHash=@hash WHERE RowId=1 AND LastAppliedIndex=@before AND HeadHash=@previous AND DatasetId=@dataset AND NodeId=@node",
            CancellationToken.None,("@idx",entry.LogIndex),("@hash",entry.EventHash.ToArray()),("@before",node.Index),
            ("@previous",node.Hash),("@dataset",options.DatasetId),("@node",options.NodeId))==1,StoreRejection.PrefixConflict);
    }
}
