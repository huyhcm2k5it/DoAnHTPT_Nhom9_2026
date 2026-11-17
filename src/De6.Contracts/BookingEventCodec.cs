using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace De6.Contracts;

public static class BookingEventCodec
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    internal static byte[] CanonicalBytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Options);
    private static JsonSerializerOptions CreateOptions()
    {
        var options=new JsonSerializerOptions
        {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
            MaxDepth=32
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper,allowIntegerValues:false));
        options.Converters.Add(new UtcConverter());
        return options;
    }

    public static PersistedEvent Encode(BookingEvent value)
    {
        Validate(value);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Options);
        if(bytes.Length>1024*1024) throw new ArgumentException("Event exceeds protocol limit");
        var prefix=Convert.FromHexString(value.PreviousHash);
        var input=new byte[32+bytes.Length];
        prefix.CopyTo(input,0); bytes.CopyTo(input,32);
        return new(value.DatasetId,value.LogIndex,value.Epoch,value.WriterId,value.EventId,bytes,
            SHA256.HashData(input),BookingCanonical.ResultBytes(value.Result));
    }

    public static BookingEvent Decode(PersistedEvent entry)
    {
        var value=Read(entry.PayloadBytes.Span);
        var canonical=Encode(value);
        if(entry.DatasetId!=value.DatasetId || entry.LogIndex!=value.LogIndex || entry.Epoch!=value.Epoch ||
           entry.WriterId!=value.WriterId || entry.EventId!=value.EventId ||
           !CryptographicOperations.FixedTimeEquals(entry.EventHash.Span,canonical.EventHash.Span) ||
           !entry.ResultPayload.Span.SequenceEqual(canonical.ResultPayload.Span))
            throw new ArgumentException("Event envelope/hash/result does not match its payload");
        return value;
    }

    public static BookingEvent Read(ReadOnlySpan<byte> payload)
    {
        if(payload.Length is 0 or >1024*1024) throw new ArgumentException("Invalid event size");
        var value=JsonSerializer.Deserialize<BookingEvent>(payload,Options) ?? throw new ArgumentException("Null event");
        Validate(value);
        if(!payload.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(value,Options)))
            throw new ArgumentException("Event payload is not canonical v1");
        return value;
    }

    private static void Validate(BookingEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.ProtocolVersion!=1 || value.DatasetId==Guid.Empty || value.EventId==Guid.Empty ||
           value.RequestId==Guid.Empty || value.LogIndex<1 || value.Epoch<1 || value.WriterId is <1 or >3 ||
           value.BeforeSeatVersion<0 || value.BeforeSeatVersion>=value.LogIndex ||
           value.Decision is null || value.Result is null || value.CommandData is null || !Enum.IsDefined(value.Decision.Kind))
            throw new ArgumentException("Invalid event metadata");
        var commandHash=BookingCanonical.CommandHash(value.DatasetId,value.OwnerId,value.CommandData);
        if(commandHash!=value.CommandHash || value.PreviousHash is null || value.PreviousHash.Length!=64 ||
           value.PreviousHash.Any(c=>!char.IsAsciiHexDigit(c) || char.IsUpper(c)))
            throw new ArgumentException("Invalid command or previous hash");
        if(value.Result.TripId!=value.CommandData.TripId || value.Result.SeatNo!=value.CommandData.SeatNo ||
           value.Result.ReservationId==Guid.Empty || value.Result.TicketId==Guid.Empty)
            throw new ArgumentException("Invalid result identity");
    }

    private sealed class UtcConverter : JsonConverter<DateTime>
    {
        private const string Format="yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
        public override DateTime Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
            => DateTime.ParseExact(reader.GetString()!,Format,CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
        public override void Write(Utf8JsonWriter writer,DateTime value,JsonSerializerOptions options)
        {
            if(value.Kind!=DateTimeKind.Utc) throw new ArgumentException("Event timestamps must be UTC");
            writer.WriteStringValue(value.ToString(Format,CultureInfo.InvariantCulture));
        }
    }
}
