using De6.Contracts;

namespace De6.Client.Sdk;

public enum PendingPhase { Sending, Unknown, Rejected }

/// <summary>A command whose FINAL result is not yet known. Kept apart from confirmed seat state.</summary>
public sealed record PendingItem(Guid RequestId, BookingAction Action, int SeatNo, DateTime SavedUtc, PendingPhase Phase, string Detail)
{
    public string ShortId => RequestId.ToString("N")[..8];
}

/// <summary>Confirmed seat state (from an authoritative snapshot) plus separate pending decoration.</summary>
public sealed record SeatCell(int SeatNo, SeatStatus? Status, bool HeldByMe, Guid? ReservationId, PendingItem? Pending);

public sealed record SeatActions(bool CanHold, bool CanConfirm, bool CanCancel)
{
    public static SeatActions None { get; } = new(false, false, false);
}

public sealed record TraceEntry(DateTime Utc, string Level, string Message)
{
    public override string ToString() => $"{Utc.ToLocalTime():HH:mm:ss.fff} [{Level}] {Message}";
}

public sealed record LastResult(Guid RequestId, int SeatNo, string Code, string Text, bool Success, DateTime Utc);

public sealed record ClientMetrics(int Sent, int Final, int Unresolved, int Rejected, int ResolvedLater, double AverageMs, double P95Ms, double MaxMs);

public static class ResultMessages
{
    public static string Describe(string code) => code switch
    {
        ResultCodes.OkHeld => "Đã giữ ghế",
        ResultCodes.OkSold => "Đã xác nhận mua, vé đã phát hành",
        ResultCodes.OkCancelled => "Đã hủy giữ ghế",
        ResultCodes.SeatBusy => "Ghế đang được quầy khác giữ",
        ResultCodes.AlreadySold => "Ghế đã bán",
        ResultCodes.AlreadyConfirmed => "Reservation đã được xác nhận trước đó",
        ResultCodes.ReservationCancelled => "Reservation đã bị hủy",
        ResultCodes.AlreadyCancelled => "Reservation đã hủy trước đó",
        ResultCodes.TicketNotCancellable => "Vé đã bán, không thể hủy",
        ResultCodes.NotOwner => "Reservation không thuộc quầy này",
        ResultCodes.ReservationNotFound => "Không tìm thấy reservation",
        ResultCodes.ReservationSeatMismatch => "Reservation không khớp số ghế",
        ResultCodes.RequestConflict => "RequestId bị trùng với nội dung khác",
        ResultCodes.RetryLater => "Cluster bận, thử lại sau",
        ResultCodes.ClusterNotReady => "Cluster chưa sẵn sàng",
        _ => code
    };

    public static bool IsSuccess(string code) => code is ResultCodes.OkHeld or ResultCodes.OkSold or ResultCodes.OkCancelled;
}
