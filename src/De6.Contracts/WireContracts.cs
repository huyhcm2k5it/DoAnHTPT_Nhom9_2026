using System.Text.Json;

namespace De6.Contracts;

public enum ClientNodeRole
{
    Unknown,
    Recovering,
    BackupReady,
    Election,
    Candidate,
    PrimaryReady,
    Maintenance,
    BlockedUncertain,
    Fenced,
    StorageFault
}

public sealed record ClientNodeStatus(
    int NodeId,
    Guid DatasetId,
    ClientNodeRole Role,
    long Epoch,
    int AcceptedLeaderId,
    bool SqlHealthy,
    long LocalAppliedIndex,
    long StableIndex,
    long FollowerLag);

public sealed record ClientEnvelope(
    int ProtocolVersion,
    Guid DatasetId,
    Guid MessageId,
    Guid CorrelationId,
    int SenderId,
    JsonElement Payload,
    Guid? RequestId = null);

public sealed record ClientRequest(Guid? RequestId, JsonElement Payload);

public static class WireMessageTypes
{
    public const string Login = "LOGIN";
    public const string GetClusterStatus = "GET_CLUSTER_STATUS";
    public const string GetSeats = "GET_SEATS";
    public const string GetMyReservations = "GET_MY_RESERVATIONS";
    public const string GetResult = "GET_RESULT";
    public const string Hold = "HOLD";
    public const string Confirm = "CONFIRM";
    public const string Cancel = "CANCEL";
    public const string AppendApply = "APPEND_APPLY";
    public const string AckApplied = "ACK_APPLIED";
    public const string CheckBackup = "CHECK_BACKUP";
    public const string Heartbeat = "HEARTBEAT";
    public const string ElectionNotice = "ELECTION_NOTICE";
}
