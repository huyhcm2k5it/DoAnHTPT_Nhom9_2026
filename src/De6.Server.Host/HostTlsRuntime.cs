using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using De6.Booking.Persistence;
using De6.Cluster.Replication;
using De6.Contracts;
using De6.Transport.Tcp;

namespace De6.Server.Host;

internal sealed record HostTlsOptions(string CertificatePath, Dictionary<int, string> PeerPins, ClientAccount[] Accounts)
{
    public static HostTlsOptions Load(string path) =>
        JsonSerializer.Deserialize<HostTlsOptions>(File.ReadAllBytes(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException("Host TLS configuration is empty.");
}

internal static class HostTlsRuntime
{
    public static async Task RunAsync(ServerOptions options, HostPersistence persistence, HostTlsOptions tls,
        SqlStoreOptions sql, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tls.CertificatePath);
        var local = options.Nodes.Single(n => n.NodeId == options.NodeId);
        var remote = options.Nodes.Where(n => n.NodeId != options.NodeId).ToArray();
        if (tls.PeerPins is null || !remote.Select(n => n.NodeId).Order().SequenceEqual(tls.PeerPins.Keys.Order()))
            throw new InvalidDataException("Provision a certificate pin for each remote peer.");
        if (tls.Accounts is null || !tls.Accounts.Select(a => a.OwnerId).Distinct().Order().SequenceEqual(new[] { "A", "B", "C" }))
            throw new InvalidDataException("Provision accounts for counters A, B and C.");

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(tls.CertificatePath,
            Environment.GetEnvironmentVariable("DE6_TLS_CERTIFICATE_PASSWORD"),
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey) throw new InvalidDataException("Host TLS certificate needs a private key.");
        var pins = new Dictionary<int, string>(tls.PeerPins)
        {
            [options.NodeId] = certificate.GetCertHashString(HashAlgorithmName.SHA256)
        };
        var authenticator = new PeerCertificateAuthenticator(options.NodeId, pins);
        var peers = remote.Select(n => new TlsPeer(n, options, tls.PeerPins[n.NodeId], certificate)).ToArray();
        await using var coordinator = persistence.CreateCoordinator(peers, HasEligibleBackupAsync);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sqlHealthy = 1;
        var clientDispatcher = new ClientRequestDispatcher(options.DatasetId, options.NodeId, persistence.Control,
            coordinator, new ClientSessionManager(tls.Accounts), () => Volatile.Read(ref sqlHealthy) == 1);
        var election = new HostElectionWorker(persistence.Control, options.Nodes, async (id, payload, token) =>
        {
            await peers.Single(peer => peer.TargetNodeId == id).ExchangeAsync(payload, token).ConfigureAwait(false);
        });
        var peerDispatcher = new PeerRequestDispatcher(options.NodeId, options.DatasetId, persistence.Receiver,
            () => persistence.Control.Snapshot, election.HandleAsync);
        var address = IPAddress.TryParse(local.Host, out var ip)
            ? ip : (await Dns.GetHostAddressesAsync(local.Host, AddressFamily.InterNetwork, stop.Token)).First();
        using var clientListener = new TcpListener(address, local.ClientPort);
        using var peerListener = new TcpListener(address, local.PeerPort);
        clientListener.Start();
        peerListener.Start();
        Console.WriteLine($"TLS listeners active on S{options.NodeId}, client {local.ClientPort}, peer {local.PeerPort}.");

        var endpoints = new[]
        {
            ListenAsync(clientListener, false, stop.Token), ListenAsync(peerListener, true, stop.Token),
            MonitorSqlAsync(stop.Token), election.RunAsync(stop.Token)
        };
        try { await await Task.WhenAny(endpoints).ConfigureAwait(false); }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(endpoints).ConfigureAwait(false);
        }

        async Task<bool> HasEligibleBackupAsync(AcceptedAuthority authority, long stableIndex, CancellationToken token)
        {
            var probes = peers.Select(peer => peer.IsReadyAsync(authority.Epoch, stableIndex, token)).ToList();
            while (probes.Count > 0)
            {
                var completed = await Task.WhenAny(probes).ConfigureAwait(false);
                if (await completed.ConfigureAwait(false)) return true;
                probes.Remove(completed);
            }
            token.ThrowIfCancellationRequested();
            return false;
        }

        async Task ListenAsync(TcpListener listener, bool peerPort, CancellationToken token)
        {
            var connections = new List<Task>();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    connections.RemoveAll(t => t.IsCompletedSuccessfully);
                    if (connections.Count >= 128)
                    {
                        var done = await Task.WhenAny(connections).WaitAsync(token).ConfigureAwait(false);
                        connections.Remove(done);
                        await done.ConfigureAwait(false);
                    }
                    var tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    connections.Add(HandleAsync(tcp, peerPort, token));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { await Task.WhenAll(connections).ConfigureAwait(false); }
        }

        async Task HandleAsync(TcpClient tcp, bool peerPort, CancellationToken token)
        {
            using (tcp)
            {
                AuthenticatedPeerIdentity? identity = null;
                await using var ssl = peerPort
                    ? new SslStream(tcp.GetStream(), false, (_, presented, _, _) =>
                    {
                        if (presented is null) return false;
                        using var remoteCertificate = X509CertificateLoader.LoadCertificate(presented.GetRawCertData());
                        try { identity = authenticator.Authenticate(remoteCertificate, DateTimeOffset.UtcNow); return true; }
                        catch (UnauthorizedAccessException) { return false; }
                    })
                    : new SslStream(tcp.GetStream());
                try
                {
                    using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate, ClientCertificateRequired = peerPort,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, handshake.Token).ConfigureAwait(false);
                    await using var channel = new StreamFrameChannel(ssl, leaveOpen: true);
                    if (peerPort)
                    {
                        if (identity is null) return;
                        await peerDispatcher.HandleConnectionAsync(channel, identity, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await clientDispatcher.HandleConnectionAsync(channel, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or AuthenticationException or SocketException or
                    OperationCanceledException or JsonException or InvalidOperationException or FormatException or ArgumentException or
                    UnauthorizedAccessException or KeyNotFoundException or InvalidDataException)
                {
                    // A malformed frame, failed handshake, or disconnected peer ends only this connection.
                }
            }
        }

        async Task MonitorSqlAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        await new SqlStorageHealth().ProbeAsync(sql, token).ConfigureAwait(false);
                        Volatile.Write(ref sqlHealthy, 1);
                    }
                    catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidDataException)
                    {
                        Volatile.Write(ref sqlHealthy, 0);
                        persistence.Control.Block("SQL_HEALTH_CHECK_FAILED");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private sealed class TlsPeer(NodeEndpoint endpoint, ServerOptions local, string pin, X509Certificate2 certificate) : IPeerChannel
    {
        public int TargetNodeId => endpoint.NodeId;

        public async Task<bool> IsReadyAsync(long epoch, long stableIndex, CancellationToken cancellationToken)
        {
            try
            {
                var response = await ExchangeAsync(new { type = WireMessageTypes.CheckBackup, epoch, appliedIndex = stableIndex },
                    cancellationToken).ConfigureAwait(false);
                return response.Payload.GetProperty("status").GetString() == "OK";
            }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or
                OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException)
            {
                return false;
            }
        }

        public async Task<ClientEnvelope> ExchangeAsync(object payload, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            await using var tls = await TlsClientConnector.ConnectAsync(endpoint.Host, endpoint.PeerPort, pin,
                deadline.Token, certificate).ConfigureAwait(false);
            var request = new ClientEnvelope(1, local.DatasetId, Guid.NewGuid(), Guid.NewGuid(), local.NodeId,
                JsonSerializer.SerializeToElement(payload));
            await tls.Channel.WriteFrameAsync(WireCodec.SerializeEnvelope(request), deadline.Token).ConfigureAwait(false);
            var response = WireCodec.DeserializeEnvelope(await tls.Channel.ReadFrameAsync(deadline.Token).ConfigureAwait(false));
            if (response.DatasetId != local.DatasetId || response.SenderId != TargetNodeId ||
                response.CorrelationId != request.CorrelationId)
                throw new InvalidDataException("Peer reply does not match the request.");
            return response;
        }

        public async Task<AppliedReceipt?> SendAppendApplyAsync(PersistedEvent persistedEvent, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await using var tls = await TlsClientConnector.ConnectAsync(endpoint.Host, endpoint.PeerPort, pin,
                deadline.Token, certificate).ConfigureAwait(false);
            var channel = new NetworkPeerChannel(TargetNodeId, local.NodeId, local.DatasetId, tls.Channel);
            return await channel.SendAppendApplyAsync(persistedEvent, timeout, deadline.Token).ConfigureAwait(false);
        }
    }
}
