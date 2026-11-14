namespace De6.Booking.Persistence;

/// <summary>Ownership anchor, not an SQL implementation or a health check.</summary>
public static class PersistenceScope
{
    public const string Specification = "V12 sections 9, 11, 14, 16; database/";
    public const string RequiredAtomicUnit = "Event + projection + RequestResults + LastAppliedIndex in one fully durable local transaction";
}
