-- Invoked by SqlDatabaseBootstrap inside the migration transaction.
-- Parameters: @node int, @dataset uniqueidentifier, @hash binary(32) SeedHash.
-- The caller has verified there is no existing application data without NodeState.
INSERT dbo.Trips(TripId,TotalSeats) VALUES(1,10);
INSERT dbo.Seats(TripId,SeatNo,Status,VersionIndex)
SELECT 1,v.n,'AVAILABLE',0 FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) v(n);
INSERT dbo.NodeState(RowId,NodeId,DatasetId,AcceptedEpoch,AcceptedLeader,LastAppliedIndex,HeadHash,SchemaVersion)
VALUES(1,@node,@dataset,0,NULL,0,@hash,1);
