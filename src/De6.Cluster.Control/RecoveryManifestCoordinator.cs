using De6.Contracts;

namespace De6.Cluster.Control;

/// <summary>
/// Completes a planned recovery run after all participants are frozen at its target prefix.
/// The caller serializes administrative runs and keeps the participants closed throughout.
/// </summary>
public static class RecoveryManifestCoordinator
{
    public static async Task<RecoveryActivationAuthorization> CommitAsync(
        RecoveryManifest manifest, IReadOnlyDictionary<int, IRecoveryManifestStore> participants,
        FenceEvidence? fenceEvidence = null, IPhysicalFenceVerifier? fenceVerifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(participants);
        manifest.Validate();
        if (!participants.Keys.Order().SequenceEqual(manifest.ParticipantNodeIds.Order()))
            throw new InvalidDataException("The supplied stores must match the manifest participants.");

        var stores = manifest.ParticipantNodeIds.Select(id => participants[id]).ToArray();
        if (stores.Any(store => store is null) ||
            stores.Distinct(ReferenceEqualityComparer.Instance).Count() != stores.Length)
            throw new InvalidDataException("Each participant needs a distinct manifest store.");

        RecoveryManifestReadbackVerifier.VerifyFence(manifest, fenceEvidence, fenceVerifier);
        foreach (var store in stores)
        {
            var active = await store.ReadActiveRecoveryManifestAsync(cancellationToken).ConfigureAwait(false);
            if (active is not null && !manifest.HasSameContents(active))
                throw new InvalidDataException("A participant has a different unfinished recovery run.");
        }

        // Persist and complete are idempotent for the exact same manifest. A retry
        // may see a mix of unstaged, staged and completed participants.
        foreach (var store in stores)
            await store.PersistRecoveryManifestAsync(manifest, cancellationToken).ConfigureAwait(false);

        var readbacks = await ReadAllAsync().ConfigureAwait(false);
        RecoveryManifestReadbackVerifier.Verify(manifest, readbacks, fenceEvidence, fenceVerifier);

        foreach (var store in stores)
            await store.CompleteRecoveryManifestAsync(manifest, cancellationToken).ConfigureAwait(false);

        readbacks = await ReadAllAsync().ConfigureAwait(false);
        if (readbacks.Any(report => !report.IsCompleted))
            throw new InvalidDataException("Recovery completion has not been read back on every participant.");
        return RecoveryManifestReadbackVerifier.Verify(manifest, readbacks, fenceEvidence, fenceVerifier);

        async Task<RecoveryManifestReadback[]> ReadAllAsync()
        {
            var reports = new RecoveryManifestReadback[stores.Length];
            for (var index = 0; index < stores.Length; index++)
            {
                var expectedId = manifest.ParticipantNodeIds[index];
                var report = await stores[index].ReadRecoveryManifestAsync(
                    manifest.RunId, manifest.TargetIndex, cancellationToken).ConfigureAwait(false);
                if (report is null || report.NodeId != expectedId)
                    throw new InvalidDataException($"Missing or mismatched readback from S{expectedId}.");
                reports[index] = report;
            }
            return reports;
        }
    }
}
