using De6.Booking.Persistence;
using De6.Cluster.Control;
using De6.Cluster.Replication;
using De6.Contracts;
using De6.Server.Host;
using De6.Client.Sdk;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

internal static class CoordinatorSqlCase
{
    internal static async Task Run(string server)
    {
        var dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
        await using var db1 = await TestDatabase.Create(server, 1, activate: false);
        await using var db2 = await TestDatabase.Create(server, 2, activate: false);
        await using var db3 = await TestDatabase.Create(server, 3, activate: false);
        var nodes = new Dictionary<int, HostPersistence>();
        var histories = new List<VerifiedRecoveryHistory>();
        var databases = new[] { db1, db2, db3 };
        for (var id = 1; id <= 3; id++)
        {
            var options = new SqlStoreOptions(databases[id - 1].ConnectionString, dataset, id);
            var node = HostPersistence.Compose(options, await new SqlStorageHealth().ProbeAsync(options));
            await node.Control.BeginMaintenanceAsync(0);
            histories.Add(await node.Store.GetVerifiedRecoveryHistoryAsync(0));
            nodes.Add(id, node);
        }

        var manifest = ClusterRecoveryPlanner.PlanBootstrap(Guid.NewGuid(), histories);
        var activation = await RecoveryManifestCoordinator.CommitAsync(manifest,
            nodes.ToDictionary(pair => pair.Key, pair => (IRecoveryManifestStore)pair.Value.Store));
        foreach (var node in nodes.Values) node.Control.ApplyVerifiedActivation(activation);

        var primary = nodes[3];
        var backup = nodes[2];
        await using var coordinator = primary.CreateCoordinator([new LocalPeer(backup.Receiver)]);
        var payload = new CommandData(BookingAction.Hold, 1, 1, null);
        var idempotencyKey = Guid.NewGuid();
        var command = new AuthenticatedCommand(new(dataset, idempotencyKey, "A",
            BookingCanonical.CommandHash(dataset, "A", payload), payload));

        var reply = await coordinator.SubmitAsync(command, CancellationToken.None);
        SqlTest.Require(reply.Status == TransportStatus.Final && reply.Result?.Code == ResultCodes.OkHeld,
            "HOLD did not reach FINAL through the Host coordinator and SQL receiver.");
        var p = await primary.Store.ReadStableResultAsync(idempotencyKey, "A", 1);
        SqlTest.Require(p is not null && primary.Control.Snapshot.StableIndex == 1 &&
            backup.Control.Snapshot.LastAppliedIndex == 1 &&
            CanonicalProtocol.DeserializeBusinessResult(p.ResultPayload.Span) == reply.Result,
            "FINAL did not match the durable SQL result and applied cursors.");
        var sameRequest = await coordinator.SubmitAsync(command, CancellationToken.None);
        var result = await coordinator.GetResultAsync(idempotencyKey, "A");
        var seats = await coordinator.GetSeatsAsync();
        var reservations = await coordinator.GetMyReservationsAsync("A");
        SqlTest.Require(sameRequest == reply && result == (reply with { AppliedIndex = 1 }) &&
            seats.AppliedIndex == 1 && seats.Seats[0].Status == SeatStatus.Held &&
            reservations.DatasetId == dataset && reservations.Epoch == 1 && reservations.AppliedIndex == 1 &&
            reservations.Reservations.Single().ReservationId == reply.Result?.ReservationId,
            "Retry or snapshot did not read the original stable SQL result at index 1.");

        await ExerciseTlsAsync(dataset, nodes, databases);
        foreach (var node in new[] { primary, backup }) await node.Control.BeginMaintenanceAsync(2);
        var first = await primary.Store.GetFrozenPrefixAsync(2);
        var second = await backup.Store.GetFrozenPrefixAsync(2);
        SqlTest.Require(first.HeadHash.Span.SequenceEqual(second.HeadHash.Span) &&
            first.BusinessDigest.Span.SequenceEqual(second.BusinessDigest.Span),
            "Primary and backup disagree on the event hash or durable projection/result.");
        Console.WriteLine("PASS Host coordinator reaches FINAL with matching primary/backup SQL and serves stable snapshots");
    }

    private static async Task ExerciseTlsAsync(Guid dataset, Dictionary<int, HostPersistence> hosts, TestDatabase[] databases)
    {
        var reserved = Enumerable.Range(0, 6).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        NodeEndpoint[] endpoints;
        try
        {
            foreach (var listener in reserved) listener.Start();
            var ports = reserved.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
            endpoints = Enumerable.Range(1, 3).Select(id => new NodeEndpoint(id, "127.0.0.1",
                ports[2 * (id - 1)], ports[2 * (id - 1) + 1])).ToArray();
        }
        finally { foreach (var listener in reserved) listener.Stop(); }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var paths = new Dictionary<int, string>();
        var pins = new Dictionary<int, string>();
        var runtimes = new List<Task>();
        try
        {
            foreach (var id in new[] { 1, 2, 3 })
            {
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder();
                names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
                using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
                pins.Add(id, cert.GetCertHashString(HashAlgorithmName.SHA256));
                var path = Path.Combine(Path.GetTempPath(), $"de6-test-{Guid.NewGuid():N}.pfx");
                paths.Add(id, path);
                await File.WriteAllBytesAsync(path, cert.Export(X509ContentType.Pfx,
                    Environment.GetEnvironmentVariable("DE6_TLS_CERTIFICATE_PASSWORD")), timeout.Token);
            }

            var testPassword = Guid.NewGuid().ToString("N");
            var accounts = new[] { "A", "B", "C" }.Select(owner =>
                ClientSessionManager.HashAccount(owner, owner, testPassword)).ToArray();
            foreach (var id in new[] { 2, 3 })
            {
                var tls = new HostTlsOptions(paths[id], pins.Where(pair => pair.Key != id).ToDictionary(), accounts);
                runtimes.Add(HostTlsRuntime.RunAsync(new(id, dataset, endpoints, $"data/servers/s{id}"),
                    hosts[id], tls, new(databases[id - 1].ConnectionString, dataset, id), timeout.Token));
            }

            var factory = new TlsClientNodeConnectionFactory(endpoint => pins[endpoint.NodeId],
                new ClientLoginRequest("A", testPassword, dataset), "A");
            await using var router = new ClientRouteManager(new(1, "A", dataset, endpoints, "data/clients/c1"), factory);
            using var payload = JsonDocument.Parse("{\"type\":\"HOLD\",\"tripId\":1,\"seatNo\":2}");
            var requestId = Guid.NewGuid();
            var response = await router.SendAsync(new ClientRequest(requestId, payload.RootElement), timeout.Token);
            SqlTest.Require(response.SenderId == 3 && response.RequestId == requestId &&
                response.Payload.GetProperty("status").GetString() == "FINAL" &&
                response.Payload.GetProperty("result").GetProperty("code").GetString() == ResultCodes.OkHeld &&
                hosts[3].Control.Snapshot.StableIndex == 2 && hosts[2].Control.Snapshot.LastAppliedIndex == 2,
                "SDK HOLD did not reach FINAL through both TLS ports and real SQL.");
            Console.WriteLine("PASS SDK LOGIN and HOLD over TLS with peer certificate validation and SQL backup ACK");
        }
        finally
        {
            timeout.Cancel();
            try { await Task.WhenAll(runtimes); }
            finally { foreach (var path in paths.Values) File.Delete(path); }
        }
    }

    private sealed class LocalPeer(ReplicaReceiver receiver) : IPeerChannel
    {
        private readonly AuthenticatedPeerIdentity _primary = TestPeerIdentityFactory.Create(2, 3);
        public int TargetNodeId => 2;

        public async Task<AppliedReceipt?> SendAppendApplyAsync(PersistedEvent persistedEvent, TimeSpan timeout,
            CancellationToken cancellationToken) =>
            await receiver.AppendAsync(1, _primary, persistedEvent.Epoch, persistedEvent, cancellationToken);
    }
}
