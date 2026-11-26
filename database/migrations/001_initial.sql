CREATE TABLE dbo.Trips (
    TripId int NOT NULL CONSTRAINT PK_Trips PRIMARY KEY,
    TotalSeats int NOT NULL,
    CONSTRAINT CK_Trips_SingleTrip CHECK (TripId=1 AND TotalSeats=10)
);
CREATE TABLE dbo.Seats (
    TripId int NOT NULL, SeatNo int NOT NULL,
    Status varchar(9) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CurrentReservationId uniqueidentifier NULL,
    CurrentOwnerId varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
    VersionIndex bigint NOT NULL,
    CONSTRAINT PK_Seats PRIMARY KEY (TripId,SeatNo),
    CONSTRAINT FK_Seats_Trip FOREIGN KEY (TripId) REFERENCES dbo.Trips(TripId),
    CONSTRAINT CK_Seats_Identity CHECK (TripId=1 AND SeatNo BETWEEN 1 AND 10 AND VersionIndex>=0),
    CONSTRAINT CK_Seats_State CHECK (
        (Status='AVAILABLE' AND CurrentReservationId IS NULL AND CurrentOwnerId IS NULL) OR
        (Status IN ('HELD','SOLD') AND CurrentReservationId IS NOT NULL AND CurrentOwnerId IS NOT NULL))
);
CREATE TABLE dbo.Reservations (
    ReservationId uniqueidentifier NOT NULL CONSTRAINT PK_Reservations PRIMARY KEY,
    TripId int NOT NULL, SeatNo int NOT NULL,
    OwnerId varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Status varchar(9) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CreatedUtc datetime2(7) NOT NULL, UpdatedIndex bigint NOT NULL,
    CONSTRAINT UQ_Reservations_Identity UNIQUE (ReservationId,TripId,SeatNo,OwnerId),
    CONSTRAINT FK_Reservations_Seat FOREIGN KEY (TripId,SeatNo) REFERENCES dbo.Seats(TripId,SeatNo),
    CONSTRAINT CK_Reservations_State CHECK (Status IN ('HELD','CONFIRMED','CANCELLED') AND UpdatedIndex>0),
    CONSTRAINT CK_Reservations_Owner CHECK (LEN(OwnerId) BETWEEN 1 AND 32 AND OwnerId NOT LIKE '%[^a-zA-Z0-9_-]%')
);
CREATE UNIQUE INDEX UX_Reservations_HeldSeat ON dbo.Reservations(TripId,SeatNo) WHERE Status='HELD';
ALTER TABLE dbo.Seats ADD CONSTRAINT FK_Seats_CurrentReservation
    FOREIGN KEY (CurrentReservationId,TripId,SeatNo,CurrentOwnerId)
    REFERENCES dbo.Reservations(ReservationId,TripId,SeatNo,OwnerId);
CREATE TABLE dbo.Tickets (
    TicketId uniqueidentifier NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY,
    ReservationId uniqueidentifier NOT NULL, TripId int NOT NULL, SeatNo int NOT NULL,
    OwnerId varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    SoldUtc datetime2(7) NOT NULL, CreatedIndex bigint NOT NULL,
    CONSTRAINT UQ_Tickets_Seat UNIQUE (TripId,SeatNo),
    CONSTRAINT UQ_Tickets_Reservation UNIQUE (ReservationId),
    CONSTRAINT FK_Tickets_Reservation FOREIGN KEY (ReservationId,TripId,SeatNo,OwnerId)
        REFERENCES dbo.Reservations(ReservationId,TripId,SeatNo,OwnerId),
    CONSTRAINT CK_Tickets_Index CHECK (CreatedIndex>0)
);
CREATE TABLE dbo.OperationLog (
    LogIndex bigint NOT NULL CONSTRAINT PK_OperationLog PRIMARY KEY,
    EventId uniqueidentifier NOT NULL CONSTRAINT UQ_OperationLog_Event UNIQUE,
    Epoch bigint NOT NULL, WriterId int NOT NULL,
    RequestId uniqueidentifier NOT NULL CONSTRAINT UQ_OperationLog_Request UNIQUE,
    PreviousHash binary(32) NOT NULL, EventHash binary(32) NOT NULL,
    PayloadBytes varbinary(max) NOT NULL,
    CONSTRAINT CK_OperationLog_Metadata CHECK (LogIndex>0 AND Epoch>0 AND WriterId BETWEEN 1 AND 3 AND DATALENGTH(PayloadBytes)>0)
);
CREATE TABLE dbo.RequestResults (
    RequestId uniqueidentifier NOT NULL CONSTRAINT PK_RequestResults PRIMARY KEY,
    OwnerId varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CommandHash binary(32) NOT NULL, LogIndex bigint NOT NULL,
    ResultPayloadBytes varbinary(max) NOT NULL,
    CONSTRAINT FK_RequestResults_Log FOREIGN KEY (LogIndex) REFERENCES dbo.OperationLog(LogIndex),
    CONSTRAINT UQ_RequestResults_Log UNIQUE (LogIndex),
    CONSTRAINT CK_RequestResults_Payload CHECK (DATALENGTH(ResultPayloadBytes)>0)
);
CREATE TABLE dbo.NodeState (
    RowId tinyint NOT NULL CONSTRAINT PK_NodeState PRIMARY KEY,
    NodeId int NOT NULL, DatasetId uniqueidentifier NOT NULL,
    AcceptedEpoch bigint NOT NULL, AcceptedLeader int NULL,
    LastAppliedIndex bigint NOT NULL, HeadHash binary(32) NOT NULL, SchemaVersion int NOT NULL,
    CONSTRAINT CK_NodeState_OneRow CHECK (RowId=1 AND NodeId BETWEEN 1 AND 3),
    CONSTRAINT CK_NodeState_Authority CHECK (
        (AcceptedEpoch=0 AND AcceptedLeader IS NULL) OR
        (AcceptedEpoch>0 AND AcceptedLeader IS NOT NULL AND AcceptedLeader BETWEEN 1 AND 3)),
    CONSTRAINT CK_NodeState_Metadata CHECK (LastAppliedIndex>=0 AND SchemaVersion=1 AND DatasetId<>'00000000-0000-0000-0000-000000000000')
);
