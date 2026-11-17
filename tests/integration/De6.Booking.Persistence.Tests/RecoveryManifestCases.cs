using De6.Booking.Persistence;
using De6.Contracts;

internal static class RecoveryManifestCases
{
    private static readonly Guid Dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");

    internal static async Task Run(string server)
    {
        await using var primaryDb = await TestDatabase.Create(server, 1);
        await using var rejectedDb = await TestDatabase.Create(server, 2);
        await using var firstDb = await TestDatabase.Create(server, 2);
        await using var secondDb = await TestDatabase.Create(server, 3);

        var primaryGate = new TestGate();
        var primary = new SqlBookingStore(new(primaryDb.ConnectionString, Dataset, 1), primaryGate, new HoldFixture());
        var command = AdvancedCases.Cmd();
        var applied = await primary.DecideAndApplyAsync(command, new AcceptedAuthority(1, 1), 1);

        var peer = new AuthenticatedPeerAuthority(1, new AcceptedAuthority(1, 1));
        var firstGate = new TestGate();
        var secondGate = new TestGate();
        var first = new SqlBookingStore(new(firstDb.ConnectionString, Dataset, 2), firstGate);
        var second = new SqlBookingStore(new(secondDb.ConnectionString, Dataset, 3), secondGate);
        await first.AppendAndApplyAsync(applied.Event, peer);
        await second.AppendAndApplyAsync(applied.Event, peer);
        firstGate.Maintenance = true;
        secondGate.Maintenance = true;

        var firstHistory = await first.GetVerifiedRecoveryHistoryAsync(1);
        var secondHistory = await second.GetVerifiedRecoveryHistoryAsync(1);
        firstHistory.Validate();
        secondHistory.Validate();
        SqlTest.Require(firstHistory.Checkpoints.Count == 2 && secondHistory.Checkpoints.Count == 2 &&
            firstHistory.HeadHash.Span.SequenceEqual(secondHistory.HeadHash.Span) &&
            firstHistory.BusinessDigest.Span.SequenceEqual(secondHistory.BusinessDigest.Span),
            "Promotion participants did not produce identical audited history.");

        var manifest = new RecoveryManifest(Guid.NewGuid(), Dataset, RecoveryRunKind.Promotion,
            new AcceptedAuthority(1, 1), new AcceptedAuthority(2, 2), [2, 3], 2, 1,
            firstHistory.HeadHash, firstHistory.BusinessDigest, "sql-test-fence-001", "integration-test");

        var incorrectManifest = new RecoveryManifest(Guid.NewGuid(), Dataset, RecoveryRunKind.Promotion,
            manifest.PreviousAuthority, manifest.NewAuthority, manifest.ParticipantNodeIds, manifest.SchemaVersion,
            manifest.TargetIndex, manifest.HeadHash, System.Security.Cryptography.SHA256.HashData("wrong-digest"u8),
            "sql-test-fence-002", "integration-test");
        var rejectedGate = new TestGate { Maintenance = true };
        var rejectedStore = new SqlBookingStore(new(rejectedDb.ConnectionString, Dataset, 2), rejectedGate);
        try
        {
            await rejectedStore.PersistRecoveryManifestAsync(incorrectManifest);
            throw new Exception("Manifest with a mismatched digest was persisted.");
        }
        catch (StoreRejectedException exception) when (exception.Code == StoreRejection.InvariantViolation) { }
        SqlTest.Require(await rejectedStore.ReadActiveRecoveryManifestAsync() is null &&
            Convert.ToInt64(await SqlTest.Scalar(rejectedDb.ConnectionString, "SELECT AcceptedEpoch FROM dbo.NodeState")) == 1,
            "Rejected manifest changed durable authority or left an active run.");
        Console.WriteLine("PASS A4 SQL rejects mismatched manifest without changing authority");

        await first.PersistRecoveryManifestAsync(manifest);
        var active = await first.ReadActiveRecoveryManifestAsync();
        SqlTest.Require(active?.HasSameContents(manifest) == true,
            "First participant did not retain the exact active manifest.");
        SqlTest.Require(await second.ReadActiveRecoveryManifestAsync() is null &&
            await second.ReadRecoveryManifestAsync(manifest.RunId, 1) is null &&
            Convert.ToInt64(await SqlTest.Scalar(secondDb.ConnectionString, "SELECT AcceptedEpoch FROM dbo.NodeState")) == 1,
            "Second participant appeared committed before its own manifest write.");
        Console.WriteLine("PASS A4 single-participant write remains incomplete and restart-readable");

        await second.PersistRecoveryManifestAsync(manifest);
        var firstReadback = await first.ReadRecoveryManifestAsync(manifest.RunId, 1);
        var secondReadback = await second.ReadRecoveryManifestAsync(manifest.RunId, 1);
        SqlTest.Require(firstReadback is { AuditVerified: true } && secondReadback is { AuditVerified: true } &&
            firstReadback.Manifest.HasSameContents(manifest) && secondReadback.Manifest.HasSameContents(manifest) &&
            firstReadback.AcceptedAuthority == manifest.NewAuthority && secondReadback.AcceptedAuthority == manifest.NewAuthority &&
            firstReadback.LastAppliedIndex == manifest.TargetIndex && secondReadback.LastAppliedIndex == manifest.TargetIndex &&
            firstReadback.HeadHash.Span.SequenceEqual(secondReadback.HeadHash.Span) &&
            firstReadback.BusinessDigest.Span.SequenceEqual(secondReadback.BusinessDigest.Span),
            "Participants did not independently read back the same manifest, authority and audited data prefix.");
        Console.WriteLine("PASS A4 both SQL participants read back the same manifest, authority and audited prefix");

        await first.CompleteRecoveryManifestAsync(manifest);
        await second.CompleteRecoveryManifestAsync(manifest);
        var completedFirst = await first.ReadRecoveryManifestAsync(manifest.RunId, 1);
        var completedSecond = await second.ReadRecoveryManifestAsync(manifest.RunId, 1);
        SqlTest.Require(completedFirst is { IsCompleted: true } && completedSecond is { IsCompleted: true } &&
            await first.ReadActiveRecoveryManifestAsync() is null && await second.ReadActiveRecoveryManifestAsync() is null,
            "Completed recovery run was not durably closed on both participants.");
        Console.WriteLine("PASS A4 completed run is durable and no longer active on both SQL participants");
    }
}
