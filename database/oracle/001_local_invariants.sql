-- Read-only LOCAL checks. @seedHash is the canonical V12 SHA256 seed supplied by the caller.
-- These queries cannot prove I07, I08, I10, I11 or I12 across a distributed cluster.
SELECT 'I01' AS InvariantId, CONVERT(bigint,CASE WHEN
 (SELECT COUNT(*) FROM dbo.Trips WHERE TripId=1 AND TotalSeats=10)=1
 AND (SELECT COUNT(*) FROM dbo.Trips)=1 AND (SELECT COUNT(*) FROM dbo.Seats)=10
 AND NOT EXISTS(SELECT 1 FROM dbo.Seats WHERE TripId<>1 OR SeatNo NOT BETWEEN 1 AND 10)
 THEN 0 ELSE 1 END) AS Violations
UNION ALL SELECT 'I02',
 (SELECT COUNT_BIG(*) FROM (SELECT TripId,SeatNo FROM dbo.Reservations WHERE Status='HELD' GROUP BY TripId,SeatNo HAVING COUNT(*)>1) a)
 +(SELECT COUNT_BIG(*) FROM (SELECT TripId,SeatNo FROM dbo.Tickets GROUP BY TripId,SeatNo HAVING COUNT(*)>1) b)
UNION ALL SELECT 'I03',
 (SELECT COUNT_BIG(*) FROM dbo.Seats s LEFT JOIN dbo.Reservations r ON r.ReservationId=s.CurrentReservationId
 LEFT JOIN dbo.Tickets t ON t.TripId=s.TripId AND t.SeatNo=s.SeatNo
 WHERE s.Status='SOLD' AND (r.ReservationId IS NULL OR r.Status<>'CONFIRMED' OR t.TicketId IS NULL
 OR t.ReservationId<>r.ReservationId OR t.OwnerId<>s.CurrentOwnerId OR r.OwnerId<>s.CurrentOwnerId))
 +(SELECT COUNT_BIG(*) FROM dbo.Tickets t JOIN dbo.Seats s ON s.TripId=t.TripId AND s.SeatNo=t.SeatNo
 WHERE s.Status<>'SOLD' OR s.CurrentReservationId<>t.ReservationId OR s.CurrentOwnerId<>t.OwnerId)
UNION ALL SELECT 'I04',
 (SELECT COUNT_BIG(*) FROM dbo.Seats s LEFT JOIN dbo.Reservations r ON r.ReservationId=s.CurrentReservationId
 WHERE (s.Status='AVAILABLE' AND (s.CurrentReservationId IS NOT NULL OR s.CurrentOwnerId IS NOT NULL))
 OR (s.Status='HELD' AND (r.ReservationId IS NULL OR r.Status<>'HELD' OR r.OwnerId<>s.CurrentOwnerId OR r.TripId<>s.TripId OR r.SeatNo<>s.SeatNo)))
UNION ALL SELECT 'I05',
 (SELECT COUNT_BIG(*) FROM dbo.OperationLog l LEFT JOIN dbo.RequestResults r ON r.LogIndex=l.LogIndex AND r.RequestId=l.RequestId WHERE r.RequestId IS NULL)
 +(SELECT COUNT_BIG(*) FROM dbo.RequestResults r LEFT JOIN dbo.OperationLog l ON l.LogIndex=r.LogIndex AND l.RequestId=r.RequestId WHERE l.RequestId IS NULL)
UNION ALL SELECT 'I06',CONVERT(bigint,CASE WHEN
 (SELECT COUNT(*) FROM dbo.NodeState)=1
 AND (SELECT LastAppliedIndex FROM dbo.NodeState WHERE RowId=1)=(SELECT COUNT_BIG(*) FROM dbo.OperationLog)
 AND (SELECT LastAppliedIndex FROM dbo.NodeState WHERE RowId=1)=COALESCE((SELECT MAX(LogIndex) FROM dbo.OperationLog),0)
 AND NOT EXISTS(SELECT 1 FROM dbo.Seats WHERE VersionIndex>(SELECT LastAppliedIndex FROM dbo.NodeState WHERE RowId=1))
 AND NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE UpdatedIndex>(SELECT LastAppliedIndex FROM dbo.NodeState WHERE RowId=1))
 THEN 0 ELSE 1 END)
UNION ALL SELECT 'I09',
 (SELECT COUNT_BIG(*) FROM (SELECT PreviousHash,LAG(EventHash,1,@seedHash) OVER(ORDER BY LogIndex) AS ExpectedHash FROM dbo.OperationLog) p WHERE PreviousHash<>ExpectedHash)
 +(SELECT COUNT_BIG(*) FROM dbo.NodeState WHERE HeadHash<>COALESCE((SELECT TOP(1) EventHash FROM dbo.OperationLog ORDER BY LogIndex DESC),@seedHash));
