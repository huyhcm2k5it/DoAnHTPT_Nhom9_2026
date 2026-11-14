# Schema contract to implement

Owner: Huy. Implement the seven tables in V12 section 9: Trips, Seats, Reservations, Tickets, OperationLog, RequestResults and NodeState. Review primary/foreign/candidate keys, filtered uniqueness for HELD and one ticket per seat/reservation. TripId=1, TotalSeats=10, SeatNo=1..10. No schema script exists yet.

Cross-table SOLD/CONFIRMED/owner and AVAILABLE/HELD relationships require transaction checks and the oracle. NodeState persists identity, accepted authority and applied prefix; it must not persist a flag that reopens serving on boot. Schema version is shared; NodeId and credentials remain local.
