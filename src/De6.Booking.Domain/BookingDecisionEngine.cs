using De6.Contracts;

namespace De6.Booking.Domain;

public static class BookingDecisionEngine
{
    public static BookingDecision Decide(
        CommandData command,
        string authenticatedOwnerId,
        SeatView currentSeat,
        ReservationRecord? currentReservation,
        TicketRecord? existingTicket,
        long nextLogIndex,
        long epoch,
        int writerId,
        DateTime utcNow,
        Guid? fixedIdForTesting = null)
    {
        if (command.TripId != 1)
        {
            var err = new BusinessResult("INVALID_TRIP_ID", command.TripId, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (command.SeatNo is < 1 or > 10)
        {
            var err = new BusinessResult("INVALID_SEAT_NO", command.TripId, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentSeat.TripId != command.TripId || currentSeat.SeatNo != command.SeatNo)
            throw new InvalidOperationException("Seat projection does not match command.");

        return command.Action switch
        {
            BookingAction.Hold => DecideHold(command, authenticatedOwnerId, currentSeat, nextLogIndex, utcNow, fixedIdForTesting),
            BookingAction.Confirm => DecideConfirm(command, authenticatedOwnerId, currentSeat, currentReservation, existingTicket, nextLogIndex, utcNow, fixedIdForTesting),
            BookingAction.Cancel => DecideCancel(command, authenticatedOwnerId, currentSeat, currentReservation, nextLogIndex, utcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(command.Action), command.Action, "Unknown action")
        };
    }

    private static BookingDecision DecideHold(
        CommandData command,
        string authenticatedOwnerId,
        SeatView currentSeat,
        long nextLogIndex,
        DateTime utcNow,
        Guid? fixedIdForTesting)
    {
        if (currentSeat.Status == SeatStatus.Available)
        {
            var reservationId = fixedIdForTesting ?? Guid.NewGuid();
            var resAfter = new ReservationRecord(
                reservationId,
                1,
                command.SeatNo,
                authenticatedOwnerId,
                ReservationStatus.Held,
                utcNow,
                nextLogIndex);

            var seatAfter = new SeatView(
                1,
                command.SeatNo,
                SeatStatus.Held,
                reservationId,
                authenticatedOwnerId,
                nextLogIndex);

            var result = new BusinessResult(ResultCodes.OkHeld, 1, command.SeatNo, reservationId, null);
            return new BookingDecision(DecisionKind.HeldCreated, seatAfter, resAfter, null, result);
        }

        if (currentSeat.Status == SeatStatus.Held)
        {
            var result = new BusinessResult(ResultCodes.SeatBusy, 1, command.SeatNo, null, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, result);
        }

        // Sold
        var soldResult = new BusinessResult(ResultCodes.AlreadySold, 1, command.SeatNo, null, null);
        return new BookingDecision(DecisionKind.NoChange, null, null, null, soldResult);
    }

    private static BookingDecision DecideConfirm(
        CommandData command,
        string authenticatedOwnerId,
        SeatView currentSeat,
        ReservationRecord? currentReservation,
        TicketRecord? existingTicket,
        long nextLogIndex,
        DateTime utcNow,
        Guid? fixedIdForTesting)
    {
        if (command.ReservationId is null)
        {
            var err = new BusinessResult(ResultCodes.ReservationNotFound, 1, command.SeatNo, null, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation is null)
        {
            var err = new BusinessResult(ResultCodes.ReservationNotFound, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.ReservationId != command.ReservationId)
            throw new InvalidOperationException("Reservation lookup returned a different identity.");

        if (currentReservation.OwnerId != authenticatedOwnerId)
        {
            // Do not leak other owner's details
            var err = new BusinessResult(ResultCodes.NotOwner, 1, command.SeatNo, null, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.TripId != 1 || currentReservation.SeatNo != command.SeatNo)
        {
            var err = new BusinessResult(ResultCodes.ReservationSeatMismatch, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.Status == ReservationStatus.Cancelled)
        {
            var err = new BusinessResult(ResultCodes.ReservationCancelled, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.Status == ReservationStatus.Confirmed)
        {
            if (currentSeat.Status != SeatStatus.Sold || currentSeat.ReservationId != currentReservation.ReservationId ||
                currentSeat.OwnerId != authenticatedOwnerId)
                throw new InvalidOperationException("Sold seat does not match confirmed reservation.");
            if (existingTicket is null || existingTicket.ReservationId != currentReservation.ReservationId ||
                existingTicket.OwnerId != authenticatedOwnerId)
                throw new InvalidOperationException("Confirmed reservation has no matching ticket.");
            var ticketId = existingTicket.TicketId;
            var err = new BusinessResult(ResultCodes.AlreadyConfirmed, 1, command.SeatNo, command.ReservationId, ticketId);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        // HELD -> CONFIRM
        EnsureHeldSeatMatchesReservation(currentSeat, currentReservation);
        var newTicketId = fixedIdForTesting ?? Guid.NewGuid();
        var resAfter = currentReservation with
        {
            Status = ReservationStatus.Confirmed,
            UpdatedIndex = nextLogIndex
        };

        var seatAfter = currentSeat with
        {
            Status = SeatStatus.Sold,
            VersionIndex = nextLogIndex
        };

        var ticketCreated = new TicketRecord(
            newTicketId,
            1,
            command.SeatNo,
            currentReservation.ReservationId,
            authenticatedOwnerId,
            utcNow,
            nextLogIndex);

        var okResult = new BusinessResult(ResultCodes.OkSold, 1, command.SeatNo, currentReservation.ReservationId, newTicketId);
        return new BookingDecision(DecisionKind.SoldConfirmed, seatAfter, resAfter, ticketCreated, okResult);
    }

    private static BookingDecision DecideCancel(
        CommandData command,
        string authenticatedOwnerId,
        SeatView currentSeat,
        ReservationRecord? currentReservation,
        long nextLogIndex,
        DateTime utcNow)
    {
        if (command.ReservationId is null)
        {
            var err = new BusinessResult(ResultCodes.ReservationNotFound, 1, command.SeatNo, null, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation is null)
        {
            var err = new BusinessResult(ResultCodes.ReservationNotFound, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.ReservationId != command.ReservationId)
            throw new InvalidOperationException("Reservation lookup returned a different identity.");

        if (currentReservation.OwnerId != authenticatedOwnerId)
        {
            var err = new BusinessResult(ResultCodes.NotOwner, 1, command.SeatNo, null, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.TripId != 1 || currentReservation.SeatNo != command.SeatNo)
        {
            var err = new BusinessResult(ResultCodes.ReservationSeatMismatch, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.Status == ReservationStatus.Cancelled)
        {
            var err = new BusinessResult(ResultCodes.AlreadyCancelled, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        if (currentReservation.Status == ReservationStatus.Confirmed)
        {
            var err = new BusinessResult(ResultCodes.TicketNotCancellable, 1, command.SeatNo, command.ReservationId, null);
            return new BookingDecision(DecisionKind.NoChange, null, null, null, err);
        }

        // HELD -> CANCEL
        EnsureHeldSeatMatchesReservation(currentSeat, currentReservation);
        var resAfter = currentReservation with
        {
            Status = ReservationStatus.Cancelled,
            UpdatedIndex = nextLogIndex
        };

        var seatAfter = currentSeat with
        {
            Status = SeatStatus.Available,
            ReservationId = null,
            OwnerId = null,
            VersionIndex = nextLogIndex
        };

        var okResult = new BusinessResult(ResultCodes.OkCancelled, 1, command.SeatNo, currentReservation.ReservationId, null);
        return new BookingDecision(DecisionKind.HoldCancelled, seatAfter, resAfter, null, okResult);
    }

    private static void EnsureHeldSeatMatchesReservation(SeatView seat, ReservationRecord reservation)
    {
        if (seat.Status != SeatStatus.Held || seat.ReservationId != reservation.ReservationId ||
            seat.OwnerId != reservation.OwnerId)
            throw new InvalidOperationException("Held seat does not match reservation.");
    }
}
