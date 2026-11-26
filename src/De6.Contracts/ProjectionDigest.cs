namespace De6.Contracts;
public static class ProjectionDigest
{
    public static byte[] Compute(Guid datasetId,long index,StorageProjection projection)
    {
        var value=new Material(1,datasetId,index,
            projection.Seats.OrderBy(s=>s.TripId).ThenBy(s=>s.SeatNo).ToArray(),
            projection.Reservations.OrderBy(r=>r.ReservationId.ToString("N"),StringComparer.Ordinal).ToArray(),
            projection.Tickets.OrderBy(t=>t.TicketId.ToString("N"),StringComparer.Ordinal).ToArray(),
            projection.Results.OrderBy(r=>r.RequestId.ToString("N"),StringComparer.Ordinal)
                .Select(r=>new Result(r.RequestId,r.OwnerId,r.CommandHash,r.LogIndex,Convert.ToBase64String(r.ResultPayload.Span))).ToArray());
        return System.Security.Cryptography.SHA256.HashData(BookingEventCodec.CanonicalBytes(value));
    }
    private sealed record Result(Guid RequestId,string OwnerId,string CommandHash,long LogIndex,string PayloadBase64);
    private sealed record Material(int Version,Guid DatasetId,long Index,SeatView[] Seats,ReservationData[] Reservations,TicketData[] Tickets,Result[] Results);
}
