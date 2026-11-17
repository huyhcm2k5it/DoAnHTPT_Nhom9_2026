using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace De6.Contracts;

public enum RecoveryRunKind { Bootstrap, Promotion, Rejoin }

/// <summary>A durable log-prefix checkpoint whose chain and projection were audited locally.</summary>
public sealed class RecoveryHistoryCheckpoint
{
    public RecoveryHistoryCheckpoint(long index, ReadOnlyMemory<byte> previousHash, ReadOnlyMemory<byte> headHash,
        ReadOnlyMemory<byte> businessDigest)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0 && previousHash.Length != 0 || index > 0 && previousHash.Length != 32)
            throw new ArgumentException("PreviousHash must be empty at the seed and SHA-256 for each event.", nameof(previousHash));
        if (headHash.Length != 32) throw new ArgumentException("HeadHash must be SHA-256.", nameof(headHash));
        if (businessDigest.Length != 32) throw new ArgumentException("BusinessDigest must be SHA-256.", nameof(businessDigest));
        Index = index;
        PreviousHash = previousHash.ToArray();
        HeadHash = headHash.ToArray();
        BusinessDigest = businessDigest.ToArray();
    }

    public long Index { get; }
    public ReadOnlyMemory<byte> PreviousHash { get; }
    public ReadOnlyMemory<byte> HeadHash { get; }
    public ReadOnlyMemory<byte> BusinessDigest { get; }
}

/// <summary>
/// Evidence returned by a node while its recovery/maintenance gate is frozen. Checkpoints
/// must cover every index from the dataset seed (0) through LastAppliedIndex without gaps.
/// </summary>
public sealed class VerifiedRecoveryHistory
{
    internal VerifiedRecoveryHistory(int nodeId, Guid datasetId, int schemaVersion,
        AcceptedAuthority? acceptedAuthority, long lastAppliedIndex, ReadOnlyMemory<byte> headHash,
        ReadOnlyMemory<byte> businessDigest, bool auditVerified, bool frozen,
        IEnumerable<RecoveryHistoryCheckpoint> checkpoints)
    {
        if (nodeId is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(nodeId));
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId is required.", nameof(datasetId));
        if (schemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (lastAppliedIndex < 0) throw new ArgumentOutOfRangeException(nameof(lastAppliedIndex));
        if (headHash.Length != 32) throw new ArgumentException("HeadHash must be SHA-256.", nameof(headHash));
        if (businessDigest.Length != 32) throw new ArgumentException("BusinessDigest must be SHA-256.", nameof(businessDigest));
        if (acceptedAuthority is { } authority && (authority.Epoch <= 0 || authority.LeaderId is < 1 or > 3))
            throw new ArgumentException("AcceptedAuthority is invalid.", nameof(acceptedAuthority));
        ArgumentNullException.ThrowIfNull(checkpoints);

        NodeId = nodeId;
        DatasetId = datasetId;
        SchemaVersion = schemaVersion;
        AcceptedAuthority = acceptedAuthority;
        LastAppliedIndex = lastAppliedIndex;
        HeadHash = headHash.ToArray();
        BusinessDigest = businessDigest.ToArray();
        AuditVerified = auditVerified;
        Frozen = frozen;
        Checkpoints = Array.AsReadOnly(checkpoints.Select(point =>
            new RecoveryHistoryCheckpoint(point.Index, point.PreviousHash, point.HeadHash, point.BusinessDigest)).ToArray());
    }

    public int NodeId { get; }
    public Guid DatasetId { get; }
    public int SchemaVersion { get; }
    public AcceptedAuthority? AcceptedAuthority { get; }
    public long LastAppliedIndex { get; }
    public ReadOnlyMemory<byte> HeadHash { get; }
    public ReadOnlyMemory<byte> BusinessDigest { get; }
    public bool AuditVerified { get; }
    public bool Frozen { get; }
    public IReadOnlyList<RecoveryHistoryCheckpoint> Checkpoints { get; }

    public void Validate()
    {
        if (!AuditVerified || !Frozen)
            throw new InvalidDataException("Recovery history must be audited and frozen before it can be compared.");
        if (LastAppliedIndex > int.MaxValue || Checkpoints.Count != LastAppliedIndex + 1)
            throw new InvalidDataException("Recovery history checkpoints are incomplete or exceed the supported range.");
        for (var index = 0; index < Checkpoints.Count; index++)
        {
            var checkpoint = Checkpoints[index];
            if (checkpoint.Index != index || checkpoint.HeadHash.Length != 32 || checkpoint.BusinessDigest.Length != 32)
                throw new InvalidDataException("Recovery history checkpoints must be contiguous and contain SHA-256 values.");
            if (index == 0 && checkpoint.PreviousHash.Length != 0 || index > 0 &&
                (checkpoint.PreviousHash.Length != 32 ||
                 !CryptographicOperations.FixedTimeEquals(checkpoint.PreviousHash.Span, Checkpoints[index - 1].HeadHash.Span)))
                throw new InvalidDataException("Recovery history checkpoint does not extend the preceding hash.");
        }

        var head = Checkpoints[^1];
        if (!CryptographicOperations.FixedTimeEquals(head.HeadHash.Span, HeadHash.Span) ||
            !CryptographicOperations.FixedTimeEquals(head.BusinessDigest.Span, BusinessDigest.Span))
            throw new InvalidDataException("The current recovery checkpoint does not match the reported head/digest.");
    }
}

/// <summary>The exact recovery decision copied to every participating database.</summary>
public sealed class RecoveryManifest
{
    public RecoveryManifest(Guid runId, Guid datasetId, RecoveryRunKind kind,
        AcceptedAuthority? previousAuthority, AcceptedAuthority newAuthority,
        IEnumerable<int> participantNodeIds, int schemaVersion, long targetIndex,
        ReadOnlyMemory<byte> headHash, ReadOnlyMemory<byte> businessDigest,
        string? fenceEvidenceReference = null, string? fenceOperatorId = null)
    {
        ArgumentNullException.ThrowIfNull(participantNodeIds);
        RunId = runId;
        DatasetId = datasetId;
        Kind = kind;
        PreviousAuthority = previousAuthority;
        NewAuthority = newAuthority;
        ParticipantNodeIds = Array.AsReadOnly(participantNodeIds.ToArray());
        SchemaVersion = schemaVersion;
        TargetIndex = targetIndex;
        HeadHash = headHash.ToArray();
        BusinessDigest = businessDigest.ToArray();
        FenceEvidenceReference = fenceEvidenceReference;
        FenceOperatorId = fenceOperatorId;
        Validate();
    }

    public Guid RunId { get; }
    public Guid DatasetId { get; }
    public RecoveryRunKind Kind { get; }
    public AcceptedAuthority? PreviousAuthority { get; }
    public AcceptedAuthority NewAuthority { get; }
    public IReadOnlyList<int> ParticipantNodeIds { get; }
    public int SchemaVersion { get; }
    public long TargetIndex { get; }
    public ReadOnlyMemory<byte> HeadHash { get; }
    public ReadOnlyMemory<byte> BusinessDigest { get; }
    public string? FenceEvidenceReference { get; }
    public string? FenceOperatorId { get; }

    public void Validate()
    {
        var expectedParticipants = Kind switch
        {
            RecoveryRunKind.Bootstrap => 3,
            RecoveryRunKind.Promotion or RecoveryRunKind.Rejoin => 2,
            _ => throw new InvalidDataException("Unknown recovery run kind.")
        };
        if (RunId == Guid.Empty || DatasetId == Guid.Empty || SchemaVersion <= 0 || TargetIndex < 0 ||
            HeadHash.Length != 32 || BusinessDigest.Length != 32 ||
            NewAuthority.Epoch <= 0 || NewAuthority.LeaderId is < 1 or > 3 ||
            ParticipantNodeIds.Count != expectedParticipants ||
            ParticipantNodeIds.Any(nodeId => nodeId is < 1 or > 3) ||
            ParticipantNodeIds.Distinct().Count() != expectedParticipants ||
            !ParticipantNodeIds.Contains(NewAuthority.LeaderId))
            throw new InvalidDataException("Recovery manifest contains invalid identity, authority, participants or prefix data.");

        var previous = PreviousAuthority;
        if (Kind == RecoveryRunKind.Bootstrap)
        {
            if (PreviousAuthority is not null || NewAuthority != new AcceptedAuthority(1, 3) ||
                TargetIndex != 0 || !ParticipantNodeIds.Order().SequenceEqual(new[] { 1, 2, 3 }) ||
                FenceEvidenceReference is not null || FenceOperatorId is not null)
                throw new InvalidDataException("Bootstrap must activate epoch 1 on S3 after verifying all three empty seeded nodes.");
        }
        else if (Kind == RecoveryRunKind.Promotion &&
                 (previous is not { } promotionAuthority || promotionAuthority.Epoch <= 0 ||
                  promotionAuthority.LeaderId is < 1 or > 3 || promotionAuthority.Epoch == long.MaxValue ||
                  NewAuthority.Epoch != promotionAuthority.Epoch + 1 || NewAuthority.LeaderId == promotionAuthority.LeaderId ||
                  ParticipantNodeIds.Contains(promotionAuthority.LeaderId) ||
                  string.IsNullOrWhiteSpace(FenceEvidenceReference) || string.IsNullOrWhiteSpace(FenceOperatorId) ||
                  FenceEvidenceReference.Length > 128 || FenceOperatorId.Length > 32 ||
                  FenceEvidenceReference.Contains("|", StringComparison.Ordinal) || FenceOperatorId.Contains("|", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Promotion must advance one epoch, record fence evidence and elect a different leader.");
        }
        else if (Kind == RecoveryRunKind.Rejoin &&
                 (NewAuthority.Epoch <= 0 || previous is { } rejoinAuthority &&
                    (rejoinAuthority.Epoch <= 0 || rejoinAuthority.LeaderId is < 1 or > 3 ||
                     NewAuthority.Epoch < rejoinAuthority.Epoch ||
                     NewAuthority.Epoch == rejoinAuthority.Epoch && NewAuthority.LeaderId != rejoinAuthority.LeaderId) ||
                  FenceEvidenceReference is not null || FenceOperatorId is not null))
        {
            throw new InvalidDataException("Rejoin may adopt only the current authority and does not require a promotion fence.");
        }
    }

    public byte[] ComputeHash()
    {
        Validate();
        var old = PreviousAuthority is { } authority
            ? $"{authority.Epoch.ToString(CultureInfo.InvariantCulture)}:{authority.LeaderId.ToString(CultureInfo.InvariantCulture)}"
            : "0:0";
        var participants = string.Join(",", ParticipantNodeIds.Order().Select(nodeId => nodeId.ToString(CultureInfo.InvariantCulture)));
        var canonical = string.Join("|", "DE6-RECOVERY-MANIFEST-1", RunId.ToString("N"), DatasetId.ToString("N"),
            Kind.ToString(), old,
            $"{NewAuthority.Epoch.ToString(CultureInfo.InvariantCulture)}:{NewAuthority.LeaderId.ToString(CultureInfo.InvariantCulture)}",
            participants, SchemaVersion.ToString(CultureInfo.InvariantCulture),
            TargetIndex.ToString(CultureInfo.InvariantCulture), Convert.ToHexString(HeadHash.Span),
            Convert.ToHexString(BusinessDigest.Span), FenceEvidenceReference ?? string.Empty,
            FenceOperatorId ?? string.Empty);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    public bool HasSameContents(RecoveryManifest? other)
        => other is not null && CryptographicOperations.FixedTimeEquals(ComputeHash(), other.ComputeHash());
}

/// <summary>One participant's durable manifest, authority and frozen storage readback.</summary>
public sealed class RecoveryManifestReadback
{
    internal RecoveryManifestReadback(int nodeId, RecoveryManifest manifest, AcceptedAuthority? acceptedAuthority,
        int schemaVersion, long lastAppliedIndex, ReadOnlyMemory<byte> headHash,
        ReadOnlyMemory<byte> businessDigest, bool auditVerified, bool isCompleted = false)
    {
        NodeId = nodeId;
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        AcceptedAuthority = acceptedAuthority;
        SchemaVersion = schemaVersion;
        LastAppliedIndex = lastAppliedIndex;
        HeadHash = headHash.ToArray();
        BusinessDigest = businessDigest.ToArray();
        AuditVerified = auditVerified;
        IsCompleted = isCompleted;
    }

    public int NodeId { get; }
    public RecoveryManifest Manifest { get; }
    public AcceptedAuthority? AcceptedAuthority { get; }
    public int SchemaVersion { get; }
    public long LastAppliedIndex { get; }
    public ReadOnlyMemory<byte> HeadHash { get; }
    public ReadOnlyMemory<byte> BusinessDigest { get; }
    public bool AuditVerified { get; }
    public bool IsCompleted { get; }
}

/// <summary>Ports for a Host adapter to obtain recovery evidence from a real SQL participant.</summary>
public interface IRecoveryEvidenceStore
{
    Task<VerifiedRecoveryHistory> GetVerifiedRecoveryHistoryAsync(long expectedIndex,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable local manifest/authority transaction and read-back interface.</summary>
public interface IRecoveryManifestStore
{
    Task PersistRecoveryManifestAsync(RecoveryManifest manifest, CancellationToken cancellationToken = default);
    Task<RecoveryManifest?> ReadActiveRecoveryManifestAsync(CancellationToken cancellationToken = default);
    Task<RecoveryManifestReadback?> ReadRecoveryManifestAsync(Guid runId, long expectedIndex,
        CancellationToken cancellationToken = default);
    Task CompleteRecoveryManifestAsync(RecoveryManifest manifest, CancellationToken cancellationToken = default);
}
