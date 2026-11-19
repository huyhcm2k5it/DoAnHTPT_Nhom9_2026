using System.Security.Cryptography;
using De6.Contracts;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

public sealed partial class SqlBookingStore
{
    private sealed record StoredRecoveryManifest(RecoveryManifest Manifest, string Status, bool IsActive);

    public async Task PersistRecoveryManifestAsync(RecoveryManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        ValidateLocalParticipant(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = gate.EnterMaintenance(manifest.TargetIndex);

        await Transaction(async (connection, transaction, node) =>
        {
            Require(node.Index == manifest.TargetIndex, StoreRejection.IndexMismatch, node.Index);
            Require(node.SchemaVersion == manifest.SchemaVersion, StoreRejection.NotReady);
            var currentAuthority = Authority(node);
            var existing = await ReadManifest(connection, transaction, manifest.RunId);
            if (existing is not null)
            {
                Require(existing.Manifest.HasSameContents(manifest), StoreRejection.InvariantViolation);
                Require(currentAuthority == manifest.NewAuthority, StoreRejection.StaleAuthority);
                var auditExisting = await Audit(connection, transaction, node);
                Require(auditExisting.MatchesHistory &&
                    CryptographicOperations.FixedTimeEquals(auditExisting.BusinessDigest.Span, manifest.BusinessDigest.Span) &&
                    CryptographicOperations.FixedTimeEquals(node.Hash, manifest.HeadHash.Span), StoreRejection.InvariantViolation);
                return (true, (PersistedEvent?)null);
            }

            var active = await SqlSupport.Scalar(connection, transaction,
                "SELECT TOP(1) RunId FROM dbo.RecoveryManifest WITH(UPDLOCK,HOLDLOCK) WHERE IsActive=1", CancellationToken.None);
            Require(active is null or DBNull, StoreRejection.NotReady);
            Require(IsAuthorityAllowedForStaging(currentAuthority, manifest), StoreRejection.StaleAuthority);

            var audit = await Audit(connection, transaction, node);
            Require(audit.MatchesHistory &&
                CryptographicOperations.FixedTimeEquals(audit.BusinessDigest.Span, manifest.BusinessDigest.Span) &&
                CryptographicOperations.FixedTimeEquals(node.Hash, manifest.HeadHash.Span), StoreRejection.InvariantViolation);

            await InsertManifest(connection, transaction, manifest);
            var updated = await SqlSupport.Execute(connection, transaction,
                "UPDATE dbo.NodeState SET AcceptedEpoch=@epoch,AcceptedLeader=@leader WHERE RowId=1",
                CancellationToken.None, ("@epoch", manifest.NewAuthority.Epoch), ("@leader", manifest.NewAuthority.LeaderId));
            Require(updated == 1, StoreRejection.InvariantViolation);
            return (true, (PersistedEvent?)null);
        }, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecoveryManifest?> ReadActiveRecoveryManifestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = gate.EnterRecoveryInspection();
        return await Transaction(async (connection, transaction, _) =>
        {
            var run = await SqlSupport.Scalar(connection, transaction,
                "SELECT RunId FROM dbo.RecoveryManifest WITH(UPDLOCK,HOLDLOCK) WHERE IsActive=1", CancellationToken.None);
            if (run is null or DBNull) return ((RecoveryManifest?)null, (PersistedEvent?)null);
            var stored = await ReadManifest(connection, transaction, (Guid)run);
            Require(stored is { IsActive: true }, StoreRejection.InvariantViolation);
            ValidateLocalParticipant(stored.Manifest);
            ValidateManifestHash(stored.Manifest, await ReadStoredManifestHash(connection, transaction, stored.Manifest.RunId));
            return (stored.Manifest, (PersistedEvent?)null);
        }, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecoveryManifestReadback?> ReadRecoveryManifestAsync(Guid runId, long expectedIndex,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("RunId is required.", nameof(runId));
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = gate.EnterMaintenance(expectedIndex);
        return await Transaction(async (connection, transaction, node) =>
        {
            Require(node.Index == expectedIndex, StoreRejection.IndexMismatch, node.Index);
            var stored = await ReadManifest(connection, transaction, runId);
            if (stored is null) return ((RecoveryManifestReadback?)null, (PersistedEvent?)null);
            var manifest = stored.Manifest;
            ValidateLocalParticipant(manifest);
            ValidateManifestHash(manifest, await ReadStoredManifestHash(connection, transaction, runId));
            Require(manifest.TargetIndex == node.Index && manifest.SchemaVersion == node.SchemaVersion &&
                Authority(node) == manifest.NewAuthority, StoreRejection.StaleAuthority);
            var audit = await Audit(connection, transaction, node);
            Require(audit.MatchesHistory &&
                CryptographicOperations.FixedTimeEquals(node.Hash, manifest.HeadHash.Span) &&
                CryptographicOperations.FixedTimeEquals(audit.BusinessDigest.Span, manifest.BusinessDigest.Span),
                StoreRejection.InvariantViolation);
            var readback = new RecoveryManifestReadback(options.NodeId, manifest, Authority(node),
                node.SchemaVersion, node.Index, node.Hash, audit.BusinessDigest, audit.MatchesHistory,
                isCompleted: stored.Status == "COMPLETED" && !stored.IsActive);
            return (readback, (PersistedEvent?)null);
        }, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteRecoveryManifestAsync(RecoveryManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        ValidateLocalParticipant(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = gate.EnterMaintenance(manifest.TargetIndex);
        await Transaction(async (connection, transaction, node) =>
        {
            Require(node.Index == manifest.TargetIndex && node.SchemaVersion == manifest.SchemaVersion &&
                Authority(node) == manifest.NewAuthority, StoreRejection.StaleAuthority);
            var stored = await ReadManifest(connection, transaction, manifest.RunId);
            Require(stored is not null && stored.Manifest.HasSameContents(manifest), StoreRejection.NotReady);
            var audit = await Audit(connection, transaction, node);
            Require(audit.MatchesHistory &&
                CryptographicOperations.FixedTimeEquals(node.Hash, manifest.HeadHash.Span) &&
                CryptographicOperations.FixedTimeEquals(audit.BusinessDigest.Span, manifest.BusinessDigest.Span),
                StoreRejection.InvariantViolation);
            if (stored.Status == "COMPLETED" && !stored.IsActive)
                return (true, (PersistedEvent?)null);

            var changed = await SqlSupport.Execute(connection, transaction,
                "UPDATE dbo.RecoveryManifest SET Status='COMPLETED',IsActive=0,CompletedUtc=@now WHERE RunId=@run AND IsActive=1",
                CancellationToken.None, ("@now", DateTime.UtcNow), ("@run", manifest.RunId));
            Require(changed == 1, StoreRejection.InvariantViolation);
            return (true, (PersistedEvent?)null);
        }, null, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateLocalParticipant(RecoveryManifest manifest)
    {
        if (manifest.DatasetId != options.DatasetId || !manifest.ParticipantNodeIds.Contains(options.NodeId))
            throw new InvalidDataException("Manifest DatasetId or participant NodeId does not match this SQL store.");
    }

    private static AcceptedAuthority? Authority(Node node)
    {
        if (node.Epoch == 0 && node.Leader is null) return null;
        if (node.Epoch > 0 && node.Leader is >= 1 and <= 3) return new AcceptedAuthority(node.Epoch, node.Leader.Value);
        throw new StoreRejectedException(StoreRejection.InvariantViolation);
    }

    private static bool IsAuthorityAllowedForStaging(AcceptedAuthority? current, RecoveryManifest manifest)
    {
        if (manifest.Kind == RecoveryRunKind.Bootstrap) return current is null;
        if (manifest.Kind == RecoveryRunKind.Promotion) return current == manifest.PreviousAuthority;
        return current == manifest.PreviousAuthority || current == manifest.NewAuthority;
    }

    private async Task InsertManifest(SqlConnection connection, SqlTransaction transaction, RecoveryManifest manifest)
    {
        var participants = manifest.ParticipantNodeIds.Order().ToArray();
        await SqlSupport.Execute(connection, transaction,
            "INSERT dbo.RecoveryManifest(RunId,DatasetId,Kind,PreviousEpoch,PreviousLeaderId,NewEpoch,NewLeaderId,ParticipantCount,Participant1,Participant2,Participant3,SchemaVersion,TargetIndex,HeadHash,BusinessDigest,FenceEvidenceReference,FenceOperatorId,ManifestHash,Status,IsActive,CreatedUtc,CompletedUtc) " +
            "VALUES(@run,@dataset,@kind,@oldEpoch,@oldLeader,@newEpoch,@newLeader,@count,@p1,@p2,@p3,@schema,@index,@head,@digest,@fenceRef,@fenceOperator,@hash,'STAGED',1,@now,NULL)",
            CancellationToken.None,
            ("@run", manifest.RunId), ("@dataset", manifest.DatasetId), ("@kind", KindToDatabase(manifest.Kind)),
            ("@oldEpoch", manifest.PreviousAuthority?.Epoch), ("@oldLeader", manifest.PreviousAuthority?.LeaderId),
            ("@newEpoch", manifest.NewAuthority.Epoch), ("@newLeader", manifest.NewAuthority.LeaderId),
            ("@count", participants.Length), ("@p1", participants[0]), ("@p2", participants[1]),
            ("@p3", participants.Length == 3 ? participants[2] : null), ("@schema", manifest.SchemaVersion),
            ("@index", manifest.TargetIndex), ("@head", manifest.HeadHash.ToArray()),
            ("@digest", manifest.BusinessDigest.ToArray()), ("@fenceRef", manifest.FenceEvidenceReference),
            ("@fenceOperator", manifest.FenceOperatorId), ("@hash", manifest.ComputeHash()), ("@now", DateTime.UtcNow));
    }

    private static string KindToDatabase(RecoveryRunKind kind) => kind switch
    {
        RecoveryRunKind.Bootstrap => "BOOTSTRAP",
        RecoveryRunKind.Promotion => "PROMOTION",
        RecoveryRunKind.Rejoin => "REJOIN",
        _ => throw new InvalidDataException("Unknown recovery run kind.")
    };

    private static RecoveryRunKind KindFromDatabase(string kind) => kind switch
    {
        "BOOTSTRAP" => RecoveryRunKind.Bootstrap,
        "PROMOTION" => RecoveryRunKind.Promotion,
        "REJOIN" => RecoveryRunKind.Rejoin,
        _ => throw new StoreRejectedException(StoreRejection.InvariantViolation)
    };

    private static async Task<StoredRecoveryManifest?> ReadManifest(SqlConnection connection,
        SqlTransaction transaction, Guid runId)
    {
        using var command = SqlSupport.Command(connection, transaction,
            "SELECT DatasetId,Kind,PreviousEpoch,PreviousLeaderId,NewEpoch,NewLeaderId,ParticipantCount,Participant1,Participant2,Participant3,SchemaVersion,TargetIndex,HeadHash,BusinessDigest,FenceEvidenceReference,FenceOperatorId,Status,IsActive FROM dbo.RecoveryManifest WITH(UPDLOCK,HOLDLOCK) WHERE RunId=@run",
            ("@run", runId));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var count = reader.GetByte(6);
        var participants = count switch
        {
            2 => new[] { reader.GetInt32(7), reader.GetInt32(8) },
            3 when !reader.IsDBNull(9) => new[] { reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9) },
            _ => throw new StoreRejectedException(StoreRejection.InvariantViolation)
        };
        AcceptedAuthority? previous = reader.IsDBNull(2) && reader.IsDBNull(3)
            ? null
            : new AcceptedAuthority(reader.GetInt64(2), reader.GetInt32(3));
        var manifest = new RecoveryManifest(runId, reader.GetGuid(0), KindFromDatabase(reader.GetString(1)),
            previous, new AcceptedAuthority(reader.GetInt64(4), reader.GetInt32(5)), participants,
            reader.GetInt32(10), reader.GetInt64(11), (byte[])reader[12], (byte[])reader[13],
            reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15));
        return new StoredRecoveryManifest(manifest, reader.GetString(16), reader.GetBoolean(17));
    }

    private static async Task<byte[]> ReadStoredManifestHash(SqlConnection connection,
        SqlTransaction transaction, Guid runId)
    {
        var value = await SqlSupport.Scalar(connection, transaction,
            "SELECT ManifestHash FROM dbo.RecoveryManifest WITH(UPDLOCK,HOLDLOCK) WHERE RunId=@run",
            CancellationToken.None, ("@run", runId));
        return value is byte[] hash ? hash : throw new StoreRejectedException(StoreRejection.NotReady);
    }

    private static void ValidateManifestHash(RecoveryManifest manifest, byte[] storedHash)
    {
        if (storedHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(manifest.ComputeHash(), storedHash))
            throw new StoreRejectedException(StoreRejection.InvariantViolation);
    }
}
