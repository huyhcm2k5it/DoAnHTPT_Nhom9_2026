using De6.Transport.Tcp;

namespace De6.Client.Sdk;

public sealed class ClientLoginService
{
    private readonly IClientLoginProtocol _protocol;

    public ClientLoginService(IClientLoginProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _protocol = protocol;
    }

    public async Task<ClientSession> LoginAsync(
        string host,
        int port,
        string expectedSha256,
        string username,
        string password,
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));

        await using TlsClientConnection connection = await TlsClientConnector.ConnectAsync(
            host,
            port,
            expectedSha256,
            cancellationToken).ConfigureAwait(false);
        return await LoginAsync(connection.Channel, username, password, datasetId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientSession> LoginAsync(IFrameChannel channel, string username, string password,
        Guid datasetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        Guid correlationId = Guid.NewGuid();
        ReadOnlyMemory<byte> request = _protocol.CreateLoginRequest(
            new ClientLoginRequest(username, password, datasetId),
            Guid.NewGuid(),
            correlationId);
        await channel.WriteFrameAsync(request, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> response = await channel.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        ClientSession session = _protocol.ParseLoginResponse(response, datasetId, correlationId);
        if (session.DatasetId != datasetId || session.IsExpired())
            throw new InvalidDataException("Login response returned an invalid or expired session.");
        return session;
    }
}
