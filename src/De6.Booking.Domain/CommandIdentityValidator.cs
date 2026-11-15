using System.Text.RegularExpressions;
using De6.Contracts;

namespace De6.Booking.Domain;

public sealed record ExistingRequestRecord(
    Guid RequestId,
    string OwnerId,
    string CommandHash,
    long LogIndex,
    BusinessResult Result,
    bool IsStable);

public interface IRequestResultLookup
{
    ValueTask<ExistingRequestRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    long GetStableIndex();
}

public enum ValidationStatus
{
    Valid,
    RejectedBeforeEvent,
    DuplicateStable,
    DuplicateInFlight,
    Conflict
}

public sealed record ValidationResult(
    ValidationStatus Status,
    string? ErrorCode,
    BusinessResult? CachedResult = null);

public static class CommandIdentityValidator
{
    private static readonly Regex OwnerRegex = new(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

    public static async ValueTask<ValidationResult> ValidateAndCheckDedupAsync(
        AuthenticatedCommand authenticatedCommand,
        Guid expectedDatasetId,
        IRequestResultLookup? lookup = null,
        CancellationToken cancellationToken = default)
    {
        var identity = authenticatedCommand.Identity;

        // 1. Basic structural validation
        if (identity.DatasetId == Guid.Empty || identity.DatasetId != expectedDatasetId)
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "INVALID_DATASET_ID");

        if (identity.RequestId == Guid.Empty)
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "EMPTY_REQUEST_ID");

        if (string.IsNullOrWhiteSpace(identity.OwnerId) || !OwnerRegex.IsMatch(identity.OwnerId))
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "INVALID_OWNER_ID");

        if (identity.Command.TripId != 1)
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "INVALID_TRIP_ID");

        if (identity.Command.SeatNo is < 1 or > 10)
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "INVALID_SEAT_NO");

        if (identity.Command.Action == BookingAction.Hold && identity.Command.ReservationId.HasValue)
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "HOLD_MUST_NOT_HAVE_RESERVATION_ID");

        if ((identity.Command.Action == BookingAction.Confirm || identity.Command.Action == BookingAction.Cancel) &&
            (!identity.Command.ReservationId.HasValue || identity.Command.ReservationId.Value == Guid.Empty))
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "RESERVATION_ID_REQUIRED");

        // 2. CommandHash verification
        string computedHash;
        try
        {
            computedHash = CanonicalProtocol.ComputeCommandHashString(
                identity.DatasetId,
                identity.OwnerId,
                identity.Command.Action,
                identity.Command.TripId,
                identity.Command.SeatNo,
                identity.Command.ReservationId);
        }
        catch (Exception ex)
        {
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, $"HASH_COMPUTATION_ERROR: {ex.Message}");
        }

        if (!string.Equals(identity.CommandHash, computedHash, StringComparison.OrdinalIgnoreCase))
            return new ValidationResult(ValidationStatus.RejectedBeforeEvent, "COMMAND_HASH_MISMATCH");

        // 3. Dedup check against existing store records
        if (lookup is not null)
        {
            var existing = await lookup.FindByRequestIdAsync(identity.RequestId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                var hashMatch = string.Equals(existing.CommandHash, identity.CommandHash, StringComparison.OrdinalIgnoreCase);
                var ownerMatch = string.Equals(existing.OwnerId, identity.OwnerId, StringComparison.Ordinal);

                if (hashMatch && ownerMatch)
                {
                    if (existing.IsStable && existing.LogIndex <= lookup.GetStableIndex())
                        return new ValidationResult(ValidationStatus.DuplicateStable, null, existing.Result);

                    return new ValidationResult(ValidationStatus.DuplicateInFlight, "IN_FLIGHT_NOT_STABLE");
                }

                // Conflict: same RequestId but different hash or owner
                return new ValidationResult(ValidationStatus.Conflict, ResultCodes.RequestConflict);
            }
        }

        return new ValidationResult(ValidationStatus.Valid, null);
    }
}
