using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using De6.Booking.Domain;
using De6.Cluster.Control;
using De6.Cluster.Election;
using De6.Contracts;

var runner = new Checks();
await runner.RunAsync();
return runner.Failures == 0 ? 0 : 1;

internal sealed class Checks
{
    private readonly Guid _datasetId = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
    private int _count;

    public int Failures { get; private set; }

    public async Task RunAsync()
    {
        await Run("A1 boot ignores persisted serving authority", BootAlwaysStartsClosed);
        await Run("A1 rejects malformed and regressing persisted/activated authority", AuthorityCannotRegress);
        await Run("A1 election candidate cannot open the serving gate", CandidateDoesNotActivate);
        await Run("A3 closed node rejects SQL grants", ClosedNodeRejectsAllServingLeases);
        await Run("A3 stable primary serializes writes and stable snapshots", PrimaryGateEnforcesIndexes);
        await Run("A3 maintenance closes admission and waits for active SQL lease", MaintenanceWaitsForLeases);
        await Run("A3 maintenance blocks on an unresolved local Apply", MaintenanceBlocksUnstableApply);
        await Run("A3 block is safe while inspection lease is held", BlockDuringInspection);
        await Run("A3 replay requires exact installed context and expiry", ReplayRequiresInstalledContext);
        await Run("A3 peer certificate maps to claimed remote NodeId", PeerCertificatePinning);
        await Run("A2 Bully chooses the highest eligible candidate", BullyChoosesHighestEligible);
        await Run("A2 coordinator may be highest among currently eligible nodes", BullyAcceptsHighestEligibleCoordinator);
        await Run("A2 a higher node answers once and starts its own round", HigherNodeAnswersAndStartsRound);
        await Run("A2 answer timeout and coordinator timeout transition phases", BullyTimeoutTransitions);
        await Run("A2 rejects stale epoch and mismatched peer identity", ElectionRejectsUntrustedNotices);
        await Run("A2 an unadmitted node never answers an election", IneligibleNodeIgnoresElection);
        await Run("A2 heartbeat suspicion uses monotonic time", HeartbeatUsesMonotonicTime);
        Console.WriteLine($"{_count - Failures}/{_count} assertions passed.");
    }

    private Task BootAlwaysStartsClosed()
    {
        var control = NewControl(1, new AcceptedAuthority(7, 2), 12);
        Equal(ServingRole.Recovering, control.Snapshot.Role);
        Equal(false, control.CanServe);
        Equal(0L, control.Snapshot.StableIndex);
        Equal(12L, control.Snapshot.LastAppliedIndex);
        Equal(new AcceptedAuthority(7, 2), control.Snapshot.AcceptedAuthority);
        return Task.CompletedTask;
    }

    private Task AuthorityCannotRegress()
    {
        Throws<ArgumentException>(() => new ClusterControl(1, _datasetId, new AcceptedAuthority(0, 0)));
        Throws<ArgumentException>(() => new ClusterControl(1, _datasetId, new AcceptedAuthority(4, 0)));

        var control = NewControl(1, new AcceptedAuthority(7, 2), 12);
        Throws<InvalidOperationException>(() => control.ApplyVerifiedActivation(new VerifiedActivation(
            ServingRole.PrimaryReady, new AcceptedAuthority(6, 1), 12, 12)));
        Throws<InvalidOperationException>(() => control.ApplyVerifiedActivation(new VerifiedActivation(
            ServingRole.BackupReady, new AcceptedAuthority(7, 3), 12, 12)));
        Throws<InvalidOperationException>(() => control.ApplyVerifiedActivation(new VerifiedActivation(
            ServingRole.BackupReady, new AcceptedAuthority(7, 2), 12, 12)));
        Equal(ServingRole.Recovering, control.Snapshot.Role);
        Equal(new AcceptedAuthority(7, 2), control.Snapshot.AcceptedAuthority);

        var newerAuthority = NewControl(1, new AcceptedAuthority(7, 2), 12);
        newerAuthority.ApplyVerifiedActivation(new VerifiedActivation(ServingRole.PrimaryReady,
            new AcceptedAuthority(8, 1), 12, 12));
        Equal(ServingRole.PrimaryReady, newerAuthority.Snapshot.Role);
        Equal(new AcceptedAuthority(8, 1), newerAuthority.Snapshot.AcceptedAuthority);
        return Task.CompletedTask;
    }

    private Task CandidateDoesNotActivate()
    {
        var control = NewControl(1);
        var round = new ElectionRound(3, Guid.NewGuid());
        control.RecordElectionCandidate(3, round, 0);
        Equal(3, control.Snapshot.ElectionCandidateId);
        Equal(ServingRole.Recovering, control.Snapshot.Role);
        Equal(false, control.CanServe);
        return Task.CompletedTask;
    }

    private Task ClosedNodeRejectsAllServingLeases()
    {
        var control = NewControl(1);
        Throws<InvalidOperationException>(() => control.EnterPrimary(new AcceptedAuthority(1, 1), 1));
        Throws<InvalidOperationException>(() => control.EnterAppend(new AuthenticatedPeerAuthority(2, new AcceptedAuthority(1, 2))));
        Throws<InvalidOperationException>(() => control.EnterSnapshot(0));
        Throws<InvalidOperationException>(() => control.EnterMaintenance(0));
        return Task.CompletedTask;
    }

    private Task PrimaryGateEnforcesIndexes()
    {
        var control = NewControl(1);
        control.ApplyVerifiedActivation(new VerifiedActivation(ServingRole.PrimaryReady,
            new AcceptedAuthority(1, 1), 4, 4));

        using (control.EnterSnapshot(4))
        {
            Throws<InvalidOperationException>(() => control.EnterPrimary(new AcceptedAuthority(1, 1), 5));
        }

        using (control.EnterPrimary(new AcceptedAuthority(1, 1), 5))
        {
            Throws<InvalidOperationException>(() => control.EnterPrimary(new AcceptedAuthority(1, 1), 5));
        }
        control.RecordLocalAppliedIndex(5);
        Throws<InvalidOperationException>(() => control.EnterPrimary(new AcceptedAuthority(1, 1), 6));
        control.PublishStableIndex(5);
        using (control.EnterSnapshot(5)) { }

        Throws<InvalidOperationException>(() => control.EnterSnapshot(4));
        Throws<InvalidOperationException>(() => control.PublishStableIndex(7));
        return Task.CompletedTask;
    }

    private async Task MaintenanceWaitsForLeases()
    {
        var control = NewControl(1);
        control.ApplyVerifiedActivation(new VerifiedActivation(ServingRole.PrimaryReady,
            new AcceptedAuthority(1, 1), 0, 0));
        var lease = control.EnterSnapshot(0);
        var maintenance = control.BeginMaintenanceAsync(0);
        Equal(false, control.CanServe);
        Equal(false, maintenance.IsCompleted);
        Throws<InvalidOperationException>(() => control.EnterPrimary(new AcceptedAuthority(1, 1), 1));
        lease.Dispose();
        await maintenance;
        Equal(ServingRole.Maintenance, control.Snapshot.Role);
        using (control.EnterMaintenance(0)) { }
    }

    private async Task MaintenanceBlocksUnstableApply()
    {
        var control = NewControl(1);
        control.ApplyVerifiedActivation(new VerifiedActivation(ServingRole.PrimaryReady,
            new AcceptedAuthority(1, 1), 4, 4));
        using (control.EnterPrimary(new AcceptedAuthority(1, 1), 5)) { }
        control.RecordLocalAppliedIndex(5);
        await ThrowsAsync<InvalidOperationException>(() => control.BeginMaintenanceAsync(5));
        Equal(ServingRole.BlockedUncertain, control.Snapshot.Role);
        Equal(false, control.CanServe);
    }

    private Task BlockDuringInspection()
    {
        var control = NewControl(2);
        using (control.EnterRecoveryInspection()) control.Block("INVARIANT_VIOLATION");
        Equal(ServingRole.BlockedUncertain, control.Snapshot.Role);
        Equal(false, control.CanServe);
        using (control.EnterRecoveryInspection()) { }
        return Task.CompletedTask;
    }

    private Task ReplayRequiresInstalledContext()
    {
        var clock = new ManualTimeProvider();
        var control = new ClusterControl(2, _datasetId, timeProvider: clock);
        var context = new RecoveryContext(Guid.NewGuid(), _datasetId, 1, 2, 2, 1, 0,
            new byte[32], 1, SHA256.HashData("target"u8));
        Throws<InvalidOperationException>(() => control.EnterReplay(context));
        control.InstallRecoveryContext(context, clock.GetUtcNow().AddMinutes(1));
        using (control.EnterReplay(context)) { }

        var wrongRun = context with { RunId = Guid.NewGuid() };
        Throws<InvalidOperationException>(() => control.EnterReplay(wrongRun));
        control.RevokeRecoveryContext(context.RunId);
        Throws<InvalidOperationException>(() => control.EnterReplay(context));

        var expiring = context with { RunId = Guid.NewGuid() };
        control.InstallRecoveryContext(expiring, clock.GetUtcNow().AddSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        Throws<InvalidOperationException>(() => control.EnterReplay(expiring));
        return Task.CompletedTask;
    }

    private Task PeerCertificatePinning()
    {
        using var certificates = new TestCertificates();
        var authenticator = certificates.CreateAuthenticator(localNodeId: 1);
        var identity = authenticator.Authenticate(certificates.ForNode(2), 2, DateTimeOffset.UtcNow);
        Equal(2, identity.NodeId);
        Equal(certificates.PinForNode(2), identity.CertificateSha256);
        Throws<UnauthorizedAccessException>(() => authenticator.Authenticate(certificates.ForNode(2), 3, DateTimeOffset.UtcNow));
        Throws<UnauthorizedAccessException>(() => authenticator.Authenticate(certificates.ForNode(1), 1, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    private async Task BullyChoosesHighestEligible()
    {
        using var certificates = new TestCertificates();
        var transport = new RecordingElectionTransport();
        var election = new BullyElection(3, _datasetId, 4, nodeId => nodeId is 1 or 2 or 3, transport);
        var round = await election.StartElectionAsync();
        Equal(ElectionPhase.Candidate, election.Snapshot.Phase);
        Equal(3, election.Snapshot.CandidateId);
        Equal(2, transport.Sent.Count);
        True(transport.Sent.All(item => item.Notice.Kind == ElectionMessageKind.Coordinator));

        var node2 = certificates.CreateAuthenticator(1).Authenticate(certificates.ForNode(2), 2, DateTimeOffset.UtcNow);
        var announced = new ElectionNotice(ElectionMessageKind.Coordinator, 2, _datasetId, 4,
            new ElectionRound(2, Guid.NewGuid()));
        var receiver = new BullyElection(1, _datasetId, 4, nodeId => nodeId is 1 or 2 or 3,
            new RecordingElectionTransport());
        await receiver.ReceiveAsync(node2, announced, CancellationToken.None);
        Equal(ElectionPhase.Idle, receiver.Snapshot.Phase);

        var node3 = certificates.CreateAuthenticator(1).Authenticate(certificates.ForNode(3), 3, DateTimeOffset.UtcNow);
        var winningNotice = new ElectionNotice(ElectionMessageKind.Coordinator, 3, _datasetId, 4,
            new ElectionRound(3, Guid.NewGuid()));
        await receiver.ReceiveAsync(node3, winningNotice, CancellationToken.None);
        Equal(ElectionPhase.Follower, receiver.Snapshot.Phase);
        Equal(3, receiver.Snapshot.CandidateId);
        True(round.RoundId != Guid.Empty);
    }

    private async Task BullyAcceptsHighestEligibleCoordinator()
    {
        using var certificates = new TestCertificates();
        var peer = certificates.CreateAuthenticator(1)
            .Authenticate(certificates.ForNode(2), 2, DateTimeOffset.UtcNow);
        var receiver = new BullyElection(1, _datasetId, 4, nodeId => nodeId is 1 or 2,
            new RecordingElectionTransport());
        var coordinator = new ElectionNotice(ElectionMessageKind.Coordinator, 2, _datasetId, 4,
            new ElectionRound(2, Guid.NewGuid()));

        await receiver.ReceiveAsync(peer, coordinator, CancellationToken.None);
        Equal(ElectionPhase.Follower, receiver.Snapshot.Phase);
        Equal(2, receiver.Snapshot.CandidateId);
    }

    private async Task BullyTimeoutTransitions()
    {
        var transport = new RecordingElectionTransport();
        var election = new BullyElection(1, _datasetId, 2, nodeId => nodeId is 1 or 2, transport);
        var round = await election.StartElectionAsync();
        Equal(ElectionPhase.WaitingForAnswer, election.Snapshot.Phase);
        Equal(1, transport.Sent.Count);

        await election.OnTimeoutAsync(round, ElectionPhase.WaitingForAnswer);
        Equal(ElectionPhase.Candidate, election.Snapshot.Phase);
        Equal(1, election.Snapshot.CandidateId);

        var restartedTransport = new RecordingElectionTransport();
        var restarted = new BullyElection(1, _datasetId, 2, nodeId => nodeId is 1 or 2, restartedTransport);
        var oldRound = await restarted.StartElectionAsync();
        var peer = Identity(2);
        await restarted.ReceiveAsync(peer, new ElectionNotice(ElectionMessageKind.Answer, 2,
            _datasetId, 2, oldRound), CancellationToken.None);
        Equal(ElectionPhase.WaitingForCoordinator, restarted.Snapshot.Phase);
        await restarted.OnTimeoutAsync(oldRound, ElectionPhase.WaitingForCoordinator);
        Equal(ElectionPhase.WaitingForAnswer, restarted.Snapshot.Phase);
        True(restarted.Snapshot.Round != oldRound);
    }

    private async Task HigherNodeAnswersAndStartsRound()
    {
        using var certificates = new TestCertificates();
        var authenticator = certificates.CreateAuthenticator(localNodeId: 2);
        var sender = authenticator.Authenticate(certificates.ForNode(1), 1, DateTimeOffset.UtcNow);
        var transport = new RecordingElectionTransport();
        var election = new BullyElection(2, _datasetId, 5, nodeId => nodeId is 1 or 2 or 3, transport);
        var incomingRound = new ElectionRound(1, Guid.NewGuid());
        var incoming = new ElectionNotice(ElectionMessageKind.Election, 1, _datasetId, 5, incomingRound);

        await election.ReceiveAsync(sender, incoming, CancellationToken.None);
        Equal(ElectionPhase.WaitingForAnswer, election.Snapshot.Phase);
        True(election.Snapshot.Round!.InitiatorId == 2);
        Equal(2, transport.Sent.Count);
        Equal(ElectionMessageKind.Answer, transport.Sent[0].Notice.Kind);
        Equal(1, transport.Sent[0].TargetNodeId);
        Equal(ElectionMessageKind.Election, transport.Sent[1].Notice.Kind);
        Equal(3, transport.Sent[1].TargetNodeId);

        var ownRound = election.Snapshot.Round;
        await election.ReceiveAsync(sender, incoming, CancellationToken.None);
        Equal(3, transport.Sent.Count);
        Equal(ownRound, election.Snapshot.Round);
    }

    private async Task ElectionRejectsUntrustedNotices()
    {
        var election = new BullyElection(1, _datasetId, 3, _ => true, new RecordingElectionTransport());
        var peer = Identity(2);
        var mismatch = new ElectionNotice(ElectionMessageKind.Election, 3, _datasetId, 3,
            new ElectionRound(3, Guid.NewGuid()));
        await ThrowsAsync<UnauthorizedAccessException>(async () =>
            await election.ReceiveAsync(peer, mismatch, CancellationToken.None));

        var stale = new ElectionNotice(ElectionMessageKind.Election, 2, _datasetId, 2,
            new ElectionRound(2, Guid.NewGuid()));
        await ThrowsAsync<InvalidDataException>(async () =>
            await election.ReceiveAsync(peer, stale, CancellationToken.None));
    }

    private async Task IneligibleNodeIgnoresElection()
    {
        using var certificates = new TestCertificates();
        var authenticator = certificates.CreateAuthenticator(localNodeId: 2);
        var sender = authenticator.Authenticate(certificates.ForNode(1), 1, DateTimeOffset.UtcNow);
        var transport = new RecordingElectionTransport();
        var election = new BullyElection(2, _datasetId, 5, nodeId => nodeId != 2, transport);
        var incoming = new ElectionNotice(ElectionMessageKind.Election, 1, _datasetId, 5,
            new ElectionRound(1, Guid.NewGuid()));

        await election.ReceiveAsync(sender, incoming, CancellationToken.None);
        Equal(0, transport.Sent.Count);
        Equal(ElectionPhase.Idle, election.Snapshot.Phase);
    }

    private Task HeartbeatUsesMonotonicTime()
    {
        using var certificates = new TestCertificates();
        var authenticator = certificates.CreateAuthenticator(localNodeId: 1);
        var clock = new ManualTimeProvider();
        var detector = new HeartbeatFailureDetector(1, [1, 2, 3], clock);
        Equal(0, detector.GetSuspectedNodeIds().Count);
        clock.Advance(TimeSpan.FromMilliseconds(2999));
        Equal(0, detector.GetSuspectedNodeIds().Count);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        True(detector.GetSuspectedNodeIds().SequenceEqual(new[] { 2, 3 }));

        var peer = authenticator.Authenticate(certificates.ForNode(2), 2, DateTimeOffset.UtcNow);
        detector.Observe(peer);
        True(detector.GetSuspectedNodeIds().SequenceEqual(new[] { 3 }));
        return Task.CompletedTask;
    }

    private ClusterControl NewControl(int nodeId, AcceptedAuthority? authority = null, long lastAppliedIndex = 0)
        => new(nodeId, _datasetId, authority, lastAppliedIndex);

    private AuthenticatedPeerIdentity Identity(int nodeId)
    {
        using var certificates = new TestCertificates();
        return certificates.CreateAuthenticator(nodeId == 1 ? 2 : 1)
            .Authenticate(certificates.ForNode(nodeId), nodeId, DateTimeOffset.UtcNow);
    }

    private async Task Run(string name, Func<Task> assertion)
    {
        _count++;
        try
        {
            await assertion();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            Failures++;
            Console.Error.WriteLine($"FAIL {name}: {exception}");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

internal sealed class RecordingElectionTransport : IElectionTransport
{
    public List<(int TargetNodeId, ElectionNotice Notice)> Sent { get; } = [];

    public ValueTask SendAsync(int targetNodeId, ElectionNotice notice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sent.Add((targetNodeId, notice));
        return ValueTask.CompletedTask;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
    private long _timestamp;

    public override DateTimeOffset GetUtcNow() => _utcNow;
    public override long GetTimestamp() => _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan duration)
    {
        _utcNow += duration;
        _timestamp += duration.Ticks;
    }
}

internal sealed class TestCertificates : IDisposable
{
    private readonly X509Certificate2[] _certificates;

    public TestCertificates()
    {
        _certificates = Enumerable.Range(1, 3).Select(nodeId => CreateCertificate(nodeId)).ToArray();
    }

    public X509Certificate2 ForNode(int nodeId) => _certificates[nodeId - 1];

    public string PinForNode(int nodeId)
        => ForNode(nodeId).GetCertHashString(HashAlgorithmName.SHA256);

    public PeerCertificateAuthenticator CreateAuthenticator(int localNodeId)
        => new(localNodeId, Enumerable.Range(1, 3).ToDictionary(nodeId => nodeId, PinForNode));

    public void Dispose()
    {
        foreach (var certificate in _certificates) certificate.Dispose();
    }

    private static X509Certificate2 CreateCertificate(int nodeId)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=de6-node-{nodeId}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    }
}
