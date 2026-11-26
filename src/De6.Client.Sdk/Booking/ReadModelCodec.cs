using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

/// <summary>
/// JSON shape of GET_SEATS / GET_MY_RESERVATIONS results as assumed by the client.
/// The authoritative shape is owned by the Q6 server read path (Gia Quan); this codec is the single place to adjust.
/// </summary>
public static class ReadModelCodec
{
    public static StableSnapshot ParseSeats(ReadOnlyMemory<byte> result)
    {
        using JsonDocument document = JsonDocument.Parse(result);
        JsonElement root = document.RootElement;
        Guid datasetId = Guid.Parse(root.GetProperty("datasetId").GetString()!);
        long epoch = root.GetProperty("epoch").GetInt64();
        long applied = root.GetProperty("appliedIndex").GetInt64();
        List<SeatView> seats = [];
        foreach (JsonElement item in root.GetProperty("seats").EnumerateArray())
        {
            seats.Add(new SeatView(
                item.GetProperty("tripId").GetInt32(),
                item.GetProperty("seatNo").GetInt32(),
                Enum.Parse<SeatStatus>(item.GetProperty("status").GetString()!, ignoreCase: true),
                OptionalGuid(item, "reservationId"),
                OptionalString(item, "ownerId"),
                item.GetProperty("versionIndex").GetInt64()));
        }
        if (seats.Count != 10 || seats.Select(s => s.SeatNo).Distinct().Count() != 10 || seats.Any(s => s.TripId != 1 || s.SeatNo is < 1 or > 10))
            throw new InvalidDataException("A seat snapshot must contain exactly seats 1..10 of trip 1.");
        return new StableSnapshot(datasetId, epoch, applied, seats.OrderBy(s => s.SeatNo).ToArray());
    }

    public static ReservationsSnapshot ParseReservations(ReadOnlyMemory<byte> result)
    {
        using JsonDocument document = JsonDocument.Parse(result);
        JsonElement root = document.RootElement;
        long applied = root.GetProperty("appliedIndex").GetInt64();
        List<ReservationData> list = [];
        foreach (JsonElement item in root.GetProperty("reservations").EnumerateArray())
        {
            list.Add(new ReservationData(
                Guid.Parse(item.GetProperty("reservationId").GetString()!),
                item.GetProperty("tripId").GetInt32(),
                item.GetProperty("seatNo").GetInt32(),
                item.GetProperty("ownerId").GetString()!,
                Enum.Parse<ReservationStatus>(item.GetProperty("status").GetString()!, ignoreCase: true),
                item.GetProperty("createdUtc").GetDateTime().ToUniversalTime(),
                item.GetProperty("updatedIndex").GetInt64()));
        }
        return new ReservationsSnapshot(applied, list);
    }

    public static byte[] WriteSeats(StableSnapshot snapshot)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("datasetId", snapshot.DatasetId.ToString("N"));
            writer.WriteNumber("epoch", snapshot.Epoch);
            writer.WriteNumber("appliedIndex", snapshot.AppliedIndex);
            writer.WriteStartArray("seats");
            foreach (SeatView seat in snapshot.Seats)
            {
                writer.WriteStartObject();
                writer.WriteNumber("tripId", seat.TripId);
                writer.WriteNumber("seatNo", seat.SeatNo);
                writer.WriteString("status", seat.Status.ToString().ToUpperInvariant());
                if (seat.ReservationId is { } r) writer.WriteString("reservationId", r.ToString("N")); else writer.WriteNull("reservationId");
                if (seat.OwnerId is { } o) writer.WriteString("ownerId", o); else writer.WriteNull("ownerId");
                writer.WriteNumber("versionIndex", seat.VersionIndex);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] WriteReservations(ReservationsSnapshot snapshot)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("appliedIndex", snapshot.AppliedIndex);
            writer.WriteStartArray("reservations");
            foreach (ReservationData item in snapshot.Reservations)
            {
                writer.WriteStartObject();
                writer.WriteString("reservationId", item.ReservationId.ToString("N"));
                writer.WriteNumber("tripId", item.TripId);
                writer.WriteNumber("seatNo", item.SeatNo);
                writer.WriteString("ownerId", item.OwnerId);
                writer.WriteString("status", item.Status.ToString().ToUpperInvariant());
                writer.WriteString("createdUtc", item.CreatedUtc.ToString("o"));
                writer.WriteNumber("updatedIndex", item.UpdatedIndex);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Canonical command bytes stored in the pending store and sent as the mutation payload (V12 fixture shape).</summary>
    public static byte[] WriteCanonicalCommand(CommandData command)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("action", CanonicalProtocol.ActionToString(command.Action));
            writer.WriteNumber("tripId", command.TripId);
            writer.WriteNumber("seatNo", command.SeatNo);
            if (command.ReservationId is { } id) writer.WriteString("reservationId", id.ToString("N")); else writer.WriteNull("reservationId");
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static Guid? OptionalGuid(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? Guid.Parse(e.GetString()!) : null;

    private static string? OptionalString(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
