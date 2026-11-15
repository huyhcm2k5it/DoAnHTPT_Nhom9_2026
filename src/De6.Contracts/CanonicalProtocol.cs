using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace De6.Contracts;

public static class CanonicalProtocol
{
    private static readonly Regex OwnerIdPattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex HashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    public static string ActionToString(BookingAction action) => action switch
    {
        BookingAction.Hold => "HOLD",
        BookingAction.Confirm => "CONFIRM",
        BookingAction.Cancel => "CANCEL",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action")
    };

    public static BookingAction ParseAction(string action) => action.ToUpperInvariant() switch
    {
        "HOLD" => BookingAction.Hold,
        "CONFIRM" => BookingAction.Confirm,
        "CANCEL" => BookingAction.Cancel,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action string")
    };

    public static string ComputeCommandHashString(Guid datasetId, string ownerId, BookingAction action, int tripId, int seatNo, Guid? reservationId)
    {
        var rawString = BuildCommandHashRawString(datasetId, ownerId, action, tripId, seatNo, reservationId);
        var bytes = Encoding.UTF8.GetBytes(rawString);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static byte[] ComputeCommandHashBytes(Guid datasetId, string ownerId, BookingAction action, int tripId, int seatNo, Guid? reservationId)
    {
        var rawString = BuildCommandHashRawString(datasetId, ownerId, action, tripId, seatNo, reservationId);
        var bytes = Encoding.UTF8.GetBytes(rawString);
        return SHA256.HashData(bytes);
    }

    public static string BuildCommandHashRawString(Guid datasetId, string ownerId, BookingAction action, int tripId, int seatNo, Guid? reservationId)
    {
        if (tripId != 1) throw new ArgumentException("TripId must be 1 per V12 spec", nameof(tripId));
        if (seatNo is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(seatNo), "SeatNo must be between 1 and 10");
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty", nameof(datasetId));
        if (ownerId is null || !OwnerIdPattern.IsMatch(ownerId))
            throw new ArgumentException("OwnerId must use 1..32 ASCII letters, digits, underscore or hyphen", nameof(ownerId));
        if (action == BookingAction.Hold && reservationId is not null)
            throw new ArgumentException("HOLD must not include ReservationId", nameof(reservationId));
        if (action != BookingAction.Hold && reservationId == Guid.Empty)
            throw new ArgumentException("ReservationId cannot be empty", nameof(reservationId));

        var actionStr = ActionToString(action);
        var reservationPart = action == BookingAction.Hold
            ? "-"
            : reservationId?.ToString("N").ToLowerInvariant() ?? throw new ArgumentException("ReservationId required for CONFIRM/CANCEL", nameof(reservationId));

        return $"v1|{datasetId:N}|{ownerId}|{actionStr}|1|{seatNo}|{reservationPart}";
    }

    public static byte[] ComputeSeedHashBytes(Guid datasetId)
    {
        var rawString = $"PTIT-DE6-V12-SEED|{datasetId:N}";
        var bytes = Encoding.ASCII.GetBytes(rawString);
        return SHA256.HashData(bytes);
    }

    public static string ComputeSeedHashHex(Guid datasetId)
    {
        return Convert.ToHexString(ComputeSeedHashBytes(datasetId)).ToLowerInvariant();
    }

    public static byte[] ComputeEventHashBytes(ReadOnlySpan<byte> previousHash32Bytes, ReadOnlySpan<byte> payloadBytes)
    {
        if (previousHash32Bytes.Length != 32)
            throw new ArgumentException("PreviousHash must be exactly 32 bytes", nameof(previousHash32Bytes));

        var combined = new byte[32 + payloadBytes.Length];
        previousHash32Bytes.CopyTo(combined.AsSpan(0, 32));
        payloadBytes.CopyTo(combined.AsSpan(32));
        return SHA256.HashData(combined);
    }

    public static string ComputeEventHashHex(ReadOnlySpan<byte> previousHash32Bytes, ReadOnlySpan<byte> payloadBytes)
    {
        return Convert.ToHexString(ComputeEventHashBytes(previousHash32Bytes, payloadBytes)).ToLowerInvariant();
    }

    public static byte[] SerializeBusinessResult(BusinessResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("code", result.Code);
            writer.WriteNumber("tripId", result.TripId);
            writer.WriteNumber("seatNo", result.SeatNo);
            if (result.ReservationId.HasValue)
                writer.WriteString("reservationId", result.ReservationId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("reservationId");

            if (result.TicketId.HasValue)
                writer.WriteString("ticketId", result.TicketId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("ticketId");

            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static BusinessResult DeserializeBusinessResult(ReadOnlySpan<byte> bytes)
    {
        using var doc = JsonDocument.Parse(bytes.ToArray());
        var root = doc.RootElement;
        var code = root.GetProperty("code").GetString()!;
        var tripId = root.GetProperty("tripId").GetInt32();
        var seatNo = root.GetProperty("seatNo").GetInt32();

        Guid? reservationId = null;
        if (root.TryGetProperty("reservationId", out var resElem) && resElem.ValueKind == JsonValueKind.String)
            reservationId = Guid.Parse(resElem.GetString()!);

        Guid? ticketId = null;
        if (root.TryGetProperty("ticketId", out var ticketElem) && ticketElem.ValueKind == JsonValueKind.String)
            ticketId = Guid.Parse(ticketElem.GetString()!);

        return new BusinessResult(code, tripId, seatNo, reservationId, ticketId);
    }

    public static byte[] SerializeEventPayload(EventPayloadData eventData)
    {
        if (eventData.DatasetId == Guid.Empty || eventData.RequestId == Guid.Empty || eventData.EventId == Guid.Empty ||
            eventData.LogIndex < 1 || eventData.Epoch < 1 || eventData.WriterId is < 1 or > 3)
            throw new ArgumentException("Event identity or authority is invalid", nameof(eventData));
        if (!HashPattern.IsMatch(eventData.PreviousHashHex))
            throw new ArgumentException("PreviousHash must be 32 bytes of hex", nameof(eventData));
        if (!string.Equals(eventData.CommandHash,
                ComputeCommandHashString(eventData.DatasetId, eventData.OwnerId, eventData.Command.Action,
                    eventData.Command.TripId, eventData.Command.SeatNo, eventData.Command.ReservationId),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("CommandHash does not match command", nameof(eventData));
        if (eventData.Decision.Result != eventData.Result)
            throw new ArgumentException("Decision result and event result differ", nameof(eventData));
        if (eventData.CreatedUtc.Kind != DateTimeKind.Utc ||
            eventData.Decision.ReservationAfter is { CreatedUtc.Kind: not DateTimeKind.Utc } ||
            eventData.Decision.TicketCreated is { IssuedUtc.Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Event timestamps must be UTC", nameof(eventData));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("datasetId", eventData.DatasetId.ToString("N").ToLowerInvariant());
            writer.WriteNumber("logIndex", eventData.LogIndex);
            writer.WriteNumber("epoch", eventData.Epoch);
            writer.WriteNumber("writerId", eventData.WriterId);
            writer.WriteString("eventId", eventData.EventId.ToString("N").ToLowerInvariant());
            writer.WriteString("previousHash", eventData.PreviousHashHex.ToLowerInvariant());
            writer.WriteString("requestId", eventData.RequestId.ToString("N").ToLowerInvariant());
            writer.WriteString("ownerId", eventData.OwnerId);
            writer.WriteString("commandHash", eventData.CommandHash.ToLowerInvariant());

            // command
            writer.WriteStartObject("command");
            writer.WriteString("action", ActionToString(eventData.Command.Action));
            writer.WriteNumber("tripId", eventData.Command.TripId);
            writer.WriteNumber("seatNo", eventData.Command.SeatNo);
            if (eventData.Command.ReservationId.HasValue)
                writer.WriteString("reservationId", eventData.Command.ReservationId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("reservationId");
            writer.WriteEndObject();

            // decision
            writer.WriteStartObject("decision");
            writer.WriteString("kind", eventData.Decision.Kind switch
            {
                DecisionKind.HeldCreated => "HELD_CREATED",
                DecisionKind.SoldConfirmed => "SOLD_CONFIRMED",
                DecisionKind.HoldCancelled => "HOLD_CANCELLED",
                DecisionKind.NoChange => "NO_CHANGE",
                _ => throw new ArgumentOutOfRangeException(nameof(eventData.Decision.Kind))
            });
            if (eventData.Decision.SeatAfter is not null)
            {
                writer.WriteStartObject("seatAfter");
                writer.WriteNumber("tripId", eventData.Decision.SeatAfter.TripId);
                writer.WriteNumber("seatNo", eventData.Decision.SeatAfter.SeatNo);
                writer.WriteString("status", eventData.Decision.SeatAfter.Status.ToString().ToUpperInvariant());
                if (eventData.Decision.SeatAfter.ReservationId.HasValue)
                    writer.WriteString("reservationId", eventData.Decision.SeatAfter.ReservationId.Value.ToString("N").ToLowerInvariant());
                else
                    writer.WriteNull("reservationId");
                if (eventData.Decision.SeatAfter.OwnerId is not null)
                    writer.WriteString("ownerId", eventData.Decision.SeatAfter.OwnerId);
                else
                    writer.WriteNull("ownerId");
                writer.WriteNumber("versionIndex", eventData.Decision.SeatAfter.VersionIndex);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("seatAfter");
            }

            if (eventData.Decision.ReservationAfter is not null)
            {
                writer.WriteStartObject("reservationAfter");
                writer.WriteString("reservationId", eventData.Decision.ReservationAfter.ReservationId.ToString("N").ToLowerInvariant());
                writer.WriteNumber("tripId", eventData.Decision.ReservationAfter.TripId);
                writer.WriteNumber("seatNo", eventData.Decision.ReservationAfter.SeatNo);
                writer.WriteString("ownerId", eventData.Decision.ReservationAfter.OwnerId);
                writer.WriteString("status", eventData.Decision.ReservationAfter.Status.ToString().ToUpperInvariant());
                writer.WriteString("createdUtc", eventData.Decision.ReservationAfter.CreatedUtc.ToString("o"));
                writer.WriteNumber("updatedIndex", eventData.Decision.ReservationAfter.UpdatedIndex);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("reservationAfter");
            }

            if (eventData.Decision.TicketCreated is not null)
            {
                writer.WriteStartObject("ticketCreated");
                writer.WriteString("ticketId", eventData.Decision.TicketCreated.TicketId.ToString("N").ToLowerInvariant());
                writer.WriteNumber("tripId", eventData.Decision.TicketCreated.TripId);
                writer.WriteNumber("seatNo", eventData.Decision.TicketCreated.SeatNo);
                writer.WriteString("reservationId", eventData.Decision.TicketCreated.ReservationId.ToString("N").ToLowerInvariant());
                writer.WriteString("ownerId", eventData.Decision.TicketCreated.OwnerId);
                writer.WriteString("issuedUtc", eventData.Decision.TicketCreated.IssuedUtc.ToString("o"));
                writer.WriteNumber("issuedIndex", eventData.Decision.TicketCreated.IssuedIndex);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("ticketCreated");
            }

            // result inside decision
            writer.WriteStartObject("result");
            writer.WriteString("code", eventData.Decision.Result.Code);
            writer.WriteNumber("tripId", eventData.Decision.Result.TripId);
            writer.WriteNumber("seatNo", eventData.Decision.Result.SeatNo);
            if (eventData.Decision.Result.ReservationId.HasValue)
                writer.WriteString("reservationId", eventData.Decision.Result.ReservationId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("reservationId");
            if (eventData.Decision.Result.TicketId.HasValue)
                writer.WriteString("ticketId", eventData.Decision.Result.TicketId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("ticketId");
            writer.WriteEndObject();

            writer.WriteEndObject(); // end decision

            writer.WriteNumber("beforeSeatVersion", eventData.BeforeSeatVersion);

            // result at root
            writer.WriteStartObject("result");
            writer.WriteString("code", eventData.Result.Code);
            writer.WriteNumber("tripId", eventData.Result.TripId);
            writer.WriteNumber("seatNo", eventData.Result.SeatNo);
            if (eventData.Result.ReservationId.HasValue)
                writer.WriteString("reservationId", eventData.Result.ReservationId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("reservationId");
            if (eventData.Result.TicketId.HasValue)
                writer.WriteString("ticketId", eventData.Result.TicketId.Value.ToString("N").ToLowerInvariant());
            else
                writer.WriteNull("ticketId");
            writer.WriteEndObject();

            writer.WriteString("createdUtc", eventData.CreatedUtc.ToString("o"));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
