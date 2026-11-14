using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace De6.Contracts;

public static class BookingCanonical
{
    public static string CommandHash(Guid datasetId, string owner, CommandData command)
    {
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId is required");
        ValidateOwner(owner);
        if (command.TripId != 1 || command.SeatNo is < 1 or > 10 || !Enum.IsDefined(command.Action))
            throw new ArgumentException("Invalid command");
        if (command.Action == BookingAction.Hold ? command.ReservationId != null : command.ReservationId is null || command.ReservationId == Guid.Empty)
            throw new ArgumentException("ReservationId must match action");
        var reservation = command.ReservationId?.ToString("N") ?? "-";
        var raw = string.Join('|', "v1", datasetId.ToString("N"), owner,
            command.Action.ToString().ToUpperInvariant(), "1", command.SeatNo.ToString(CultureInfo.InvariantCulture), reservation);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static void ValidateOwner(string owner)
    {
        if (string.IsNullOrEmpty(owner) || owner.Length > 32 || owner.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            throw new ArgumentException("OwnerId must contain 1..32 ASCII letters/digits/_/-");
    }

    public static byte[] ResultBytes(BusinessResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteResult(writer, result);
        return stream.ToArray();
    }

    internal static void WriteResult(Utf8JsonWriter writer, BusinessResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("code", result.Code);
        writer.WriteNumber("tripId", result.TripId);
        writer.WriteNumber("seatNo", result.SeatNo);
        WriteNullableGuid(writer, "reservationId", result.ReservationId);
        WriteNullableGuid(writer, "ticketId", result.TicketId);
        writer.WriteEndObject();
    }

    internal static void WriteNullableGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value.HasValue) writer.WriteString(name, value.Value.ToString("D"));
        else writer.WriteNull(name);
    }
}
