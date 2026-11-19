using System.Security.Cryptography;
using System.Text;
using De6.Contracts;

namespace De6.Cluster.Control;

public sealed record FenceEvidence(int FormerLeaderNodeId, string EvidenceReference, string OperatorId);

/// <summary>
/// Host supplied verifier for physical fencing evidence. There is intentionally no permissive
/// default: heartbeat suspicion or a message from the old primary is not fence evidence.
/// </summary>
public interface IPhysicalFenceVerifier
{
    bool Verify(FenceEvidence evidence, int expectedFormerLeaderNodeId);
}

public sealed record RecoveryPromotionPlan(RecoveryManifest Manifest, int? ReplaySourceNodeId,
    int? ReplayTargetNodeId, RecoveryContext? ReplayContext);

public enum RejoinPlanStatus { Ready, RequiresArchiveRepair, Blocked }

public sealed record RejoinPlan(RejoinPlanStatus Status, string Reason, RecoveryContext? ReplayContext,
    RecoveryManifest? Manifest);

/// <summary>Pure safety checks for promotion, first bootstrap and a returning node.</summary>
public static class ClusterRecoveryPlanner
{
    public static RecoveryPromotionPlan PlanPromotion(Guid runId, int candidateNodeId,
        VerifiedRecoveryHistory first, VerifiedRecoveryHistory second, FenceEvidence fenceEvidence,
        IPhysicalFenceVerifier fenceVerifier)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(fenceEvidence);
        ArgumentNullException.ThrowIfNull(fenceVerifier);
        ValidatePair(first, second);

        var previous = first.AcceptedAuthority
            ?? throw new InvalidDataException("Promotion requires an accepted authority on both participants.");
        if (second.AcceptedAuthority != previous)
            throw new InvalidDataException("Participants disagree about the accepted authority; remain closed.");
        if (first.NodeId == previous.LeaderId || second.NodeId == previous.LeaderId)
            throw new InvalidDataException("The physically fenced previous leader cannot be counted as a surviving promotion participant.");
        if (candidateNodeId is < 1 or > 3 || candidateNodeId == previous.LeaderId ||
            candidateNodeId != first.NodeId && candidateNodeId != second.NodeId)
            throw new InvalidDataException("The elected candidate must be one of the two surviving participants and differ from the fenced leader.");
        if (fenceEvidence.FormerLeaderNodeId != previous.LeaderId ||
            string.IsNullOrWhiteSpace(fenceEvidence.EvidenceReference) ||
            string.IsNullOrWhiteSpace(fenceEvidence.OperatorId) ||
            !fenceVerifier.Verify(fenceEvidence, previous.LeaderId))
            throw new UnauthorizedAccessException("Promotion requires verified physical fencing of the previously accepted leader.");

        VerifyCommonPrefix(first, second);
        var source = first.LastAppliedIndex > second.LastAppliedIndex ? first : second;
        var target = ReferenceEquals(source, first) ? second : first;
        var newAuthority = new AcceptedAuthority(checked(previous.Epoch + 1), candidateNodeId);
        var manifest = new RecoveryManifest(runId, first.DatasetId, RecoveryRunKind.Promotion,
            previous, newAuthority, new[] { first.NodeId, second.NodeId }, first.SchemaVersion,
            source.LastAppliedIndex, source.HeadHash, source.BusinessDigest,
            fenceEvidence.EvidenceReference, fenceEvidence.OperatorId);

        RecoveryContext? context = null;
        int? sourceNodeId = null;
        int? targetNodeId = null;
        if (source.LastAppliedIndex > target.LastAppliedIndex)
        {
            var baseCheckpoint = target.Checkpoints[checked((int)target.LastAppliedIndex)];
            context = new RecoveryContext(runId, first.DatasetId, source.NodeId, target.NodeId,
                newAuthority.Epoch, candidateNodeId, target.LastAppliedIndex, baseCheckpoint.HeadHash,
                source.LastAppliedIndex, source.HeadHash);
            sourceNodeId = source.NodeId;
            targetNodeId = target.NodeId;
        }

        return new(manifest, sourceNodeId, targetNodeId, context);
    }

    public static RecoveryManifest PlanBootstrap(Guid runId, IEnumerable<VerifiedRecoveryHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        var nodes = histories.ToArray();
        if (nodes.Length != 3 || !nodes.Select(history => history.NodeId).Order().SequenceEqual(new[] { 1, 2, 3 }))
            throw new InvalidDataException("Bootstrap requires one separately configured database for each of nodes 1, 2 and 3.");

        foreach (var history in nodes)
        {
            history.Validate();
            if (history.AcceptedAuthority is not null || history.LastAppliedIndex != 0 ||
                !CryptographicOperations.FixedTimeEquals(history.HeadHash.Span, SeedHash(history.DatasetId)))
                throw new InvalidDataException("Bootstrap is allowed only when every node is audited, authority-free and at the matching empty seed.");
        }
        if (nodes.Any(history => history.DatasetId != nodes[0].DatasetId || history.SchemaVersion != nodes[0].SchemaVersion))
            throw new InvalidDataException("Bootstrap participants must share one DatasetId and schema version.");
        if (nodes.Skip(1).Any(history =>
                !CryptographicOperations.FixedTimeEquals(history.HeadHash.Span, nodes[0].HeadHash.Span) ||
                !CryptographicOperations.FixedTimeEquals(history.BusinessDigest.Span, nodes[0].BusinessDigest.Span)))
            throw new InvalidDataException("Bootstrap seed hash or business digest differs between nodes.");

        return new RecoveryManifest(runId, nodes[0].DatasetId, RecoveryRunKind.Bootstrap, null,
            new AcceptedAuthority(1, 3), new[] { 1, 2, 3 }, nodes[0].SchemaVersion, 0,
            nodes[0].HeadHash, nodes[0].BusinessDigest);
    }

    public static RejoinPlan PlanRejoin(Guid runId, VerifiedRecoveryHistory activeSource,
        VerifiedRecoveryHistory returningNode)
    {
        ArgumentNullException.ThrowIfNull(activeSource);
        ArgumentNullException.ThrowIfNull(returningNode);
        try
        {
            ValidatePair(activeSource, returningNode);
            if (activeSource.AcceptedAuthority is not { } authority)
                return new(RejoinPlanStatus.Blocked, "The source has no accepted current authority.", null, null);
            if (activeSource.NodeId != authority.LeaderId)
                return new(RejoinPlanStatus.Blocked, "Rejoin must read from the current accepted primary while it is frozen.", null, null);
            if (returningNode.AcceptedAuthority is { } oldAuthority &&
                (oldAuthority.Epoch > authority.Epoch ||
                 (oldAuthority.Epoch == authority.Epoch && oldAuthority.LeaderId != authority.LeaderId)))
                return new(RejoinPlanStatus.Blocked, "The returning node has a newer or conflicting authority.", null, null);

            if (returningNode.LastAppliedIndex > activeSource.LastAppliedIndex)
                return new(RejoinPlanStatus.RequiresArchiveRepair,
                    "The returning node has a longer suffix. Archive and an explicit RepairPlan are required; automatic truncation is forbidden.", null, null);

            var commonIndex = returningNode.LastAppliedIndex;
            for (var index = 0; index <= commonIndex; index++)
            {
                var sourceCheckpoint = activeSource.Checkpoints[index];
                var returningCheckpoint = returningNode.Checkpoints[index];
                if (!CheckpointEquals(sourceCheckpoint, returningCheckpoint))
                    return new(RejoinPlanStatus.RequiresArchiveRepair,
                        $"The histories diverge at index {index}. Archive and an explicit RepairPlan are required.", null, null);
            }

            var sourceBase = returningNode.Checkpoints[checked((int)returningNode.LastAppliedIndex)];
            var context = new RecoveryContext(runId, activeSource.DatasetId, activeSource.NodeId,
                returningNode.NodeId, authority.Epoch, authority.LeaderId, returningNode.LastAppliedIndex,
                sourceBase.HeadHash, activeSource.LastAppliedIndex, activeSource.HeadHash);
            var manifest = new RecoveryManifest(runId, activeSource.DatasetId, RecoveryRunKind.Rejoin,
                returningNode.AcceptedAuthority, authority, new[] { activeSource.NodeId, returningNode.NodeId },
                activeSource.SchemaVersion, activeSource.LastAppliedIndex, activeSource.HeadHash,
                activeSource.BusinessDigest);
            return new(RejoinPlanStatus.Ready, "The returning history is a verified prefix of the frozen active source.", context, manifest);
        }
        catch (InvalidDataException exception)
        {
            return new(RejoinPlanStatus.Blocked, exception.Message, null, null);
        }
    }

    public static byte[] SeedHash(Guid datasetId)
    {
        if (datasetId == Guid.Empty) throw new ArgumentException("DatasetId is required.", nameof(datasetId));
        return SHA256.HashData(Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|" + datasetId.ToString("N")));
    }

    private static void ValidatePair(VerifiedRecoveryHistory first, VerifiedRecoveryHistory second)
    {
        first.Validate();
        second.Validate();
        if (first.NodeId == second.NodeId || first.DatasetId != second.DatasetId ||
            first.SchemaVersion != second.SchemaVersion)
            throw new InvalidDataException("Recovery participants must be distinct nodes with the same DatasetId and schema.");
        if (!CryptographicOperations.FixedTimeEquals(first.Checkpoints[0].HeadHash.Span, SeedHash(first.DatasetId)) ||
            !CryptographicOperations.FixedTimeEquals(second.Checkpoints[0].HeadHash.Span, SeedHash(second.DatasetId)))
            throw new InvalidDataException("A participant's log does not begin at the canonical dataset seed.");
    }

    private static void VerifyCommonPrefix(VerifiedRecoveryHistory first, VerifiedRecoveryHistory second)
    {
        var commonIndex = Math.Min(first.LastAppliedIndex, second.LastAppliedIndex);
        for (var index = 0; index <= commonIndex; index++)
        {
            if (!CheckpointEquals(first.Checkpoints[index], second.Checkpoints[index]))
                throw new InvalidDataException($"Participant histories diverge at index {index}; promotion is blocked.");
        }
    }

    private static bool CheckpointEquals(RecoveryHistoryCheckpoint left, RecoveryHistoryCheckpoint right)
        => left.Index == right.Index &&
           CryptographicOperations.FixedTimeEquals(left.PreviousHash.Span, right.PreviousHash.Span) &&
           CryptographicOperations.FixedTimeEquals(left.HeadHash.Span, right.HeadHash.Span) &&
           CryptographicOperations.FixedTimeEquals(left.BusinessDigest.Span, right.BusinessDigest.Span);
}

/// <summary>Issued only after every participant independently reads back the same durable manifest.</summary>
public sealed class RecoveryActivationAuthorization
{
    internal RecoveryActivationAuthorization(RecoveryManifest manifest) => Manifest = manifest;
    public RecoveryManifest Manifest { get; }
}

public static class RecoveryManifestReadbackVerifier
{
    public static RecoveryActivationAuthorization Verify(RecoveryManifest manifest,
        IEnumerable<RecoveryManifestReadback> readbacks, FenceEvidence? fenceEvidence = null,
        IPhysicalFenceVerifier? fenceVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(readbacks);
        manifest.Validate();
        VerifyFence(manifest, fenceEvidence, fenceVerifier);
        var reports = readbacks.ToArray();
        if (reports.Length != manifest.ParticipantNodeIds.Count ||
            !reports.Select(report => report.NodeId).Order().SequenceEqual(manifest.ParticipantNodeIds.Order()))
            throw new InvalidDataException("Activation requires one readback from every distinct manifest participant.");

        foreach (var report in reports)
        {
            if (!manifest.ParticipantNodeIds.Contains(report.NodeId) || !report.AuditVerified ||
                !report.Manifest.HasSameContents(manifest) || report.AcceptedAuthority != manifest.NewAuthority ||
                report.SchemaVersion != manifest.SchemaVersion || report.LastAppliedIndex != manifest.TargetIndex ||
                !CryptographicOperations.FixedTimeEquals(report.HeadHash.Span, manifest.HeadHash.Span) ||
                !CryptographicOperations.FixedTimeEquals(report.BusinessDigest.Span, manifest.BusinessDigest.Span))
                throw new InvalidDataException($"Node S{report.NodeId} has not read back the exact manifest, authority and audited prefix.");
        }
        return new RecoveryActivationAuthorization(manifest);
    }

    internal static void VerifyFence(RecoveryManifest manifest, FenceEvidence? fenceEvidence,
        IPhysicalFenceVerifier? fenceVerifier)
    {
        if (manifest.Kind == RecoveryRunKind.Promotion &&
            (manifest.PreviousAuthority is not { } previous || fenceEvidence is null || fenceVerifier is null ||
             fenceEvidence.FormerLeaderNodeId != previous.LeaderId ||
             fenceEvidence.EvidenceReference != manifest.FenceEvidenceReference ||
             fenceEvidence.OperatorId != manifest.FenceOperatorId ||
             !fenceVerifier.Verify(fenceEvidence, previous.LeaderId)))
            throw new UnauthorizedAccessException("Promotion requires freshly verified fence evidence matching the manifest.");
    }
}
