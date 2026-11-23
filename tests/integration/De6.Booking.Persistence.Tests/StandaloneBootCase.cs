using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using De6.Client.Sdk;
using De6.Contracts;
using De6.Server.Host;
using Microsoft.Data.SqlClient;

internal static class StandaloneBootCase
{
    internal static async Task Run(string sqlServer)
    {
        var dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
        await using var db1 = await TestDatabase.Create(sqlServer, 1, activate: false);
        await using var db2 = await TestDatabase.Create(sqlServer, 2, activate: false);
        await using var db3 = await TestDatabase.Create(sqlServer, 3, activate: false);
        var databases = new[] { db1, db2, db3 };
        var listeners = Enumerable.Range(0, 6).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        NodeEndpoint[] endpoints;
        try
        {
            foreach (var listener in listeners) listener.Start();
            var ports = listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
            endpoints = Enumerable.Range(1, 3).Select(id => new NodeEndpoint(id, "127.0.0.1", ports[id * 2 - 2], ports[id * 2 - 1])).ToArray();
        }
        finally { foreach (var listener in listeners) listener.Stop(); }

        var work = Directory.CreateTempSubdirectory("de6-standalone-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var running = new List<Process>();
        var pins = new Dictionary<int, string>();
        var runId = Guid.NewGuid();
        var pfxPassword = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var pendingPath = work.CreateSubdirectory("counter-a");
        try
        {
            foreach (var node in endpoints)
            {
                var id = node.NodeId;
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder();
                names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
                using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
                pins[id] = cert.GetCertHashString(HashAlgorithmName.SHA256);
                await File.WriteAllBytesAsync(Path.Combine(work.FullName, $"s{id}.pfx"), cert.Export(X509ContentType.Pfx, pfxPassword), token);
                var builder = new SqlConnectionStringBuilder(databases[id - 1].ConnectionString);
                await WriteAsync($"s{id}.json", new ServerOptions(id, dataset, endpoints, $"data/servers/s{id}",
                    new SqlOptions(builder.DataSource, builder.InitialCatalog, 15)), token);
            }
            var accounts = new[] { "A", "B", "C" }.Select(owner => ClientSessionManager.HashAccount(owner, owner, password)).ToArray();
            foreach (var id in pins.Keys)
                await WriteAsync($"tls{id}.json", new HostTlsOptions(Path.Combine(work.FullName, $"s{id}.pfx"),
                    pins.Where(p => p.Key != id).ToDictionary(), accounts), token);

            var prepare = Start(1, "--prepare-local-bootstrap");
            var prepareOut = prepare.StandardOutput.ReadToEndAsync(token);
            var prepareError = prepare.StandardError.ReadToEndAsync(token);
            await prepare.WaitForExitAsync(token);
            SqlTest.Require(prepare.ExitCode == 0, $"Bootstrap exited {prepare.ExitCode}. {await prepareOut} {await prepareError}");
            var primary = await StartListeningAsync(3);
            var factory = new TlsClientNodeConnectionFactory(endpoint => pins[endpoint.NodeId],
                new ClientLoginRequest("A", password, dataset), "A");
            await using var route = new ClientRouteManager(new(1, "A", dataset, endpoints, "data/clients/c1"), factory);
            var unavailable = await SendAsync(new { type = WireMessageTypes.Hold, tripId = 1, seatNo = 3 }, Guid.NewGuid());
            var after = await SqlTest.Scalar(db3.ConnectionString, "SELECT LastAppliedIndex FROM dbo.NodeState");
            SqlTest.Require(unavailable.Payload.GetProperty("status").GetString() == "CLUSTER_NOT_READY" &&
                Convert.ToInt64(after) == 0, "Known loss of both backups must prevent local Apply.");
            Console.WriteLine("PASS loss of both backups rejects mutation before local Apply");
            await StartListeningAsync(1);
            var survivor = await StartListeningAsync(2);

            var bookingId = Guid.NewGuid();
            var holdPayload = new { type = WireMessageTypes.Hold, tripId = 1, seatNo = 1 };
            var command = new CommandData(BookingAction.Hold, 1, 1, null);
            await using (var pending = new FileClientPendingStore(pendingPath.FullName, dataset, "A"))
                await pending.SaveBeforeSendAsync(new(dataset, bookingId, "A",
                    BookingCanonical.CommandHash(dataset, "A", command), command),
                    JsonSerializer.SerializeToUtf8Bytes(holdPayload), token);
            var hold = await SendAsync(holdPayload, bookingId);
            SqlTest.Require(hold.Payload.GetProperty("status").GetString() == "FINAL", "Standalone HOLD failed.");
            var reservationId = Guid.Parse(hold.Payload.GetProperty("result").GetProperty("reservationId").GetString()!);
            var confirm = await SendAsync(new { type = WireMessageTypes.Confirm, tripId = 1, seatNo = 1, reservationId }, Guid.NewGuid());
            SqlTest.Require(confirm.Payload.GetProperty("result").GetProperty("code").GetString() == ResultCodes.OkSold, "Standalone CONFIRM failed.");
            var read = await SendAsync(new { type = WireMessageTypes.GetResult }, bookingId);
            SqlTest.Require(read.Payload.GetProperty("appliedIndex").GetInt64() == 2 &&
                Guid.Parse(read.Payload.GetProperty("result").GetProperty("reservationId").GetString()!) == reservationId,
                "GET_RESULT after CONFIRM did not return the original HOLD result.");
            await using (var pending = new FileClientPendingStore(pendingPath.FullName, dataset, "A"))
            {
                await using var restartedRoute = new ClientRouteManager(new(1, "A", dataset, endpoints, "data/clients/c1"), factory);
                var executor = new ClientCommandExecutor(pending, restartedRoute, dataset, "A");
                var recovered = await executor.ResolvePendingAsync(bookingId, token);
                SqlTest.Require(recovered.IsFinal && CanonicalProtocol.DeserializeBusinessResult(
                    recovered.ResultPayload!.Value.Span).ReservationId == reservationId &&
                    (await executor.RestorePendingAsync(token)).Count == 0,
                    "Counter restart did not resolve the original reservation without retrying the command.");
            }
            Console.WriteLine("PASS reopened counter store resolves original RequestId over TLS and clears pending");
            var hashes = await Task.WhenAll(databases.Select(db => SqlTest.Scalar(db.ConnectionString,
                "SELECT CONVERT(varchar(64),HeadHash,2) FROM dbo.NodeState WHERE LastAppliedIndex=2")));
            SqlTest.Require(hashes[2] is string && hashes.Take(2).Any(hash => Equals(hash, hashes[2])),
                "Primary head hash must match at least one backup after FINAL.");
            Console.WriteLine("PASS three standalone Hosts bootstrap from verified SQL manifests and serve HOLD / CONFIRM / GET_RESULT over TLS");

            var contenders = await Task.WhenAll(Enumerable.Range(1, 3).Select(async clientId =>
            {
                var owner = ((char)('A' + clientId - 1)).ToString();
                var connectionFactory = new TlsClientNodeConnectionFactory(endpoint => pins[endpoint.NodeId],
                    new ClientLoginRequest(owner, password, dataset), owner);
                await using var client = new ClientRouteManager(new(clientId, owner, dataset, endpoints,
                    $"data/clients/c{clientId}"), connectionFactory);
                return await client.SendAsync(new ClientRequest(Guid.NewGuid(), JsonSerializer.SerializeToElement(
                    new { type = WireMessageTypes.Hold, tripId = 1, seatNo = 2 })), token);
            }));
            var codes = contenders.Select(reply => reply.Payload.GetProperty("result").GetProperty("code").GetString()).ToArray();
            SqlTest.Require(codes.Count(code => code == ResultCodes.OkHeld) == 1 &&
                codes.Count(code => code == ResultCodes.SeatBusy) == 2, "Exactly one of the three counters may HOLD the same seat.");
            Console.WriteLine("PASS three counter sessions race the same seat, one OK_HELD and two SEAT_BUSY");

            primary.Kill(entireProcessTree: true);
            await primary.WaitForExitAsync(token);
            var elected = false;
            while (await survivor.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (!line.StartsWith("Bully candidate S2", StringComparison.Ordinal)) continue;
                elected = true;
                break;
            }
            using var query = JsonDocument.Parse("{\"type\":\"HOLD\",\"tripId\":1,\"seatNo\":3}");
            await using var backupConnection = await factory.ConnectAsync(endpoints[1], token);
            var reply = await backupConnection.SendAsync(new ClientEnvelope(1, dataset, Guid.NewGuid(), Guid.NewGuid(), 1,
                query.RootElement, Guid.NewGuid()), token);
            SqlTest.Require(elected && reply.Payload.GetProperty("status").GetString() == "CLUSTER_NOT_READY",
                "Bully may record a candidate but must not grant writes by itself.");
            Console.WriteLine("PASS primary loss elects S2 without granting writes");

            async Task<ClientEnvelope> SendAsync(object body, Guid id) =>
                await route.SendAsync(new ClientRequest(id, JsonSerializer.SerializeToElement(body)), token);

            async Task<Process> StartListeningAsync(int id)
            {
                var process = Start(id, "--activate-local-bootstrap");
                var errors = process.StandardError.ReadToEndAsync(token);
                while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                    if (line.StartsWith("TLS listeners active", StringComparison.Ordinal)) return process;
                throw new InvalidOperationException($"S{id} exited before serving. {await errors}");
            }
        }
        finally
        {
            foreach (var process in running)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { process.Dispose(); }
            }
            foreach (var file in work.EnumerateFiles()) file.Delete();
            var datasetDirectory = new DirectoryInfo(Path.Combine(pendingPath.FullName, dataset.ToString("N")));
            var ownerDirectory = new DirectoryInfo(Path.Combine(datasetDirectory.FullName, Convert.ToHexString(SHA256.HashData("A"u8))));
            if (ownerDirectory.Exists)
            {
                foreach (var file in ownerDirectory.EnumerateFiles()) file.Delete();
                ownerDirectory.Delete();
            }
            if (datasetDirectory.Exists) datasetDirectory.Delete();
            foreach (var file in pendingPath.EnumerateFiles()) file.Delete();
            pendingPath.Delete();
            work.Delete();
        }

        Task WriteAsync<T>(string file, T value, CancellationToken cancellation) =>
            File.WriteAllBytesAsync(Path.Combine(work.FullName, file), JsonSerializer.SerializeToUtf8Bytes(value), cancellation);

        Process Start(int id, string action)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = work.FullName };
            start.Environment["DE6_HOST_TLS_CONFIG"] = Path.Combine(work.FullName, $"tls{id}.json");
            start.Environment["DE6_TLS_CERTIFICATE_PASSWORD"] = pfxPassword;
            var host = typeof(HostPersistence).Assembly.Location;
            foreach (var argument in new[] { host, "--config", Path.Combine(work.FullName, $"s{id}.json"), action, runId.ToString("D") })
                start.ArgumentList.Add(argument);
            var child = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start Host S{id}.");
            running.Add(child);
            return child;
        }
    }
}
