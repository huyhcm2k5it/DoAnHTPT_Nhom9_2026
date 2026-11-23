using System.Net;
using De6.Booking.Persistence;
using De6.Cluster.Control;
using De6.Contracts;

namespace De6.Server.Host;

/// <summary>Operator workflow for three seed databases on the same machine.</summary>
internal static class LocalBootstrap
{
    public static async Task RunAsync(string configPath, Guid runId, HostPersistence? activateNode,
        CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A bootstrap RunId is required.", nameof(runId));
        var first = ConfigurationFile.ReadServer(configPath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var configs = Enumerable.Range(1, 3).Select(id =>
            ConfigurationFile.ReadServer(Path.Combine(folder, $"s{id}.json"))).ToArray();
        if (!first.Nodes.All(node => IPAddress.TryParse(node.Host, out var address) && IPAddress.IsLoopback(address)) ||
            configs[first.NodeId - 1].Sql != first.Sql ||
            configs.Where((config, index) => config.Sql is null || config.DatasetId != first.DatasetId ||
                config.NodeId != index + 1 || !config.Nodes.OrderBy(node => node.NodeId)
                    .SequenceEqual(first.Nodes.OrderBy(node => node.NodeId))).Any() ||
            configs.Select(config => (config.Sql!.Server + "|" + config.Sql.Database))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("Local bootstrap requires three consistent loopback configurations and distinct SQL databases.");

        var nodes = new Dictionary<int, HostPersistence>();
        foreach (var config in configs)
        {
            var options = SqlPersistenceComposition.CreateStoreOptions(config);
            var health = await new SqlStorageHealth().ProbeAsync(options, cancellationToken).ConfigureAwait(false);
            if (health.LastAppliedIndex != 0)
                throw new InvalidDataException("Local bootstrap requires each database to remain at the seed. Use recovery for an existing history.");
            var composition = activateNode is not null && config.NodeId == first.NodeId
                ? activateNode : HostPersistence.Compose(options, health);
            await composition.Control.BeginMaintenanceAsync(0, cancellationToken).ConfigureAwait(false);
            nodes.Add(config.NodeId, composition);
        }

        var readbacks = new List<RecoveryManifestReadback>();
        foreach (var node in nodes.Values)
        {
            var readback = await node.Store.ReadRecoveryManifestAsync(runId, 0, cancellationToken).ConfigureAwait(false);
            if (readback is not null) readbacks.Add(readback);
        }

        if (activateNode is not null)
        {
            if (readbacks.Count != 3 || readbacks.Any(readback => !readback.IsCompleted) ||
                readbacks[0].Manifest.Kind != RecoveryRunKind.Bootstrap)
                throw new InvalidDataException("A completed bootstrap manifest must exist on all three seed databases.");
            activateNode.Control.ApplyVerifiedActivation(RecoveryManifestReadbackVerifier.Verify(readbacks[0].Manifest, readbacks));
            return;
        }

        RecoveryManifest manifest;
        if (readbacks.Count != 0)
        {
            manifest = readbacks[0].Manifest;
            if (manifest.Kind != RecoveryRunKind.Bootstrap)
                throw new InvalidDataException("RunId belongs to another kind of recovery.");
        }
        else
        {
            var histories = new List<VerifiedRecoveryHistory>();
            foreach (var node in nodes.Values)
                histories.Add(await node.Store.GetVerifiedRecoveryHistoryAsync(0, cancellationToken).ConfigureAwait(false));
            manifest = ClusterRecoveryPlanner.PlanBootstrap(runId, histories);
        }
        await RecoveryManifestCoordinator.CommitAsync(manifest,
            nodes.ToDictionary(pair => pair.Key, pair => (IRecoveryManifestStore)pair.Value.Store),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static FileStream LockNode(string configPath, int nodeId)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        return new FileStream(Path.Combine(folder, $".de6-s{nodeId}.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
    }
}
