using System.Text;
using De6.Contracts;

var failed = 0;
var dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
var command = new CommandData(BookingAction.Hold, 1, 1, null);
Check("HOLD command hash matches independent SHA256 fixture", () =>
{
    var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "command-hash.txt")).Trim();
    if (BookingCanonical.CommandHash(dataset, "A", command) != expected)
        throw new Exception("Wrong command canonicalization/hash");
});
Check("BusinessResult keeps five fields, explicit nulls and order", () =>
{
    var json = Encoding.UTF8.GetString(BookingCanonical.ResultBytes(new BusinessResult("SEAT_BUSY", 1, 1, null, null)));
    if (json != "{\"code\":\"SEAT_BUSY\",\"tripId\":1,\"seatNo\":1,\"reservationId\":null,\"ticketId\":null}")
        throw new Exception("Wrong canonical BusinessResult");
});
Check("event canonical bytes and chain hash match an independent fixture", () =>
{
    var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"event-v1.json"));
    var expectedHash=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"event-hash.txt")).Trim();
    var value=BookingEventCodec.Read(bytes);
    var entry=BookingEventCodec.Encode(value);
    if (!entry.PayloadBytes.Span.SequenceEqual(bytes) || Convert.ToHexStringLower(entry.EventHash.Span)!=expectedHash)
        throw new Exception("Wrong event encoding/hash");
    if (BookingEventCodec.Decode(entry)!=value) throw new Exception("Wrong round trip");
});
foreach(var invalid in new[]{new CommandData(BookingAction.Hold,1,0,null),new CommandData(BookingAction.Hold,1,11,null),
    new CommandData(BookingAction.Hold,2,1,null),new CommandData(BookingAction.Hold,1,1,Guid.NewGuid()),
    new CommandData(BookingAction.Confirm,1,1,null),new CommandData((BookingAction)99,1,1,null)})
    Check("reject invalid command "+invalid,()=>MustReject(()=>BookingCanonical.CommandHash(dataset,"A",invalid)));
Check("reject non-ASCII owner",()=>MustReject(()=>BookingCanonical.CommandHash(dataset,"QuầyA",command)));
Check("reject empty dataset",()=>MustReject(()=>BookingCanonical.CommandHash(Guid.Empty,"A",command)));
Check("hash is independent of current culture",()=>
{
    var old=System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
        if(BookingCanonical.CommandHash(dataset,"A",command)!=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"command-hash.txt")).Trim())
            throw new Exception("Culture changed command identity");
    }
    finally {System.Globalization.CultureInfo.CurrentCulture=old;}
});
Check("reject changed event envelope and hash",()=>
{
    var entry=BookingEventCodec.Encode(BookingEventCodec.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"event-v1.json"))));
    MustReject(()=>BookingEventCodec.Decode(entry with{LogIndex=2}));
    MustReject(()=>BookingEventCodec.Decode(entry with{EventHash=new byte[32]}));
});
Check("reject noncanonical event JSON",()=>
{
    var json=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"event-v1.json"));
    MustReject(()=>BookingEventCodec.Read(Encoding.UTF8.GetBytes(json+" ")));
});
Check("projection digest matches independent seed fixture regardless of input order",()=>
{
    var seats=Enumerable.Range(1,10).Select(n=>new SeatView(1,n,SeatStatus.Available,null,null,0)).Reverse().ToArray();
    var material=new StorageProjection(seats,Array.Empty<ReservationData>(),Array.Empty<TicketData>(),Array.Empty<DurableResultView>());
    var expected=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"projection-hash.txt")).Trim();
    if(Convert.ToHexStringLower(ProjectionDigest.Compute(dataset,0,material))!=expected) throw new Exception("Wrong canonical projection digest");
});
Console.WriteLine($"Contract failures: {failed}");
return failed == 0 ? 0 : 1;

void MustReject(Action run)
{
    try {run();throw new Exception("Invalid input accepted");}
    catch(ArgumentException) { }
}

void Check(string name, Action run)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}
