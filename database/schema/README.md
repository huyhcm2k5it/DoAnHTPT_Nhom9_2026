# Schema v1

Owner: Huy. The seven V12 tables are implemented in [migration 001](../migrations/001_initial.sql): Trips, Seats, Reservations, Tickets, OperationLog, RequestResults and NodeState. The migration includes primary/foreign/candidate keys, filtered uniqueness for HELD and one ticket per seat/reservation. TripId=1, TotalSeats=10, SeatNo=1..10. Use SqlDatabaseBootstrap via the [runbook](../../docs/implementation/HUY_WEEK1_RUNBOOK.md); do not manually rerun raw DDL.

Cross-table SOLD/CONFIRMED/owner and AVAILABLE/HELD relationships require transaction checks and the oracle. NodeState persists identity, accepted authority and applied prefix; it must not persist a flag that reopens serving on boot. Schema version is shared; NodeId and credentials remain local.
