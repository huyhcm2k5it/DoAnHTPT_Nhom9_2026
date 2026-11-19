using De6.Contracts;
using De6.Transport.Tcp;

namespace De6.Client.Sdk;

public sealed class TlsClientNodeConnectionFactory : IClientNodeConnectionFactory
{
    private readonly Func<NodeEndpoint, string> _fingerprintResolver;
    private readonly Func<TlsClientConnection, NodeEndpoint, CancellationToken, Task> _loginAsync;
    private readonly IClientNodeWireProtocol _wireProtocol;

    public TlsClientNodeConnectionFactory(
        Func<NodeEndpoint, string> fingerprintResolver,
        Func<TlsClientConnection, NodeEndpoint, CancellationToken, Task> loginAsync,
        IClientNodeWireProtocol wireProtocol)
    {
        ArgumentNullException.ThrowIfNull(fingerprintResolver);
        ArgumentNullException.ThrowIfNull(loginAsync);
        ArgumentNullException.ThrowIfNull(wireProtocol);
        _fingerprintResolver = fingerprintResolver;
        _loginAsync = loginAsync;
        _wireProtocol = wireProtocol;
    }

    public async Task<IClientNodeConnection> ConnectAsync(NodeEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string fingerprint = _fingerprintResolver(endpoint);
        TlsClientConnection connection = await TlsClientConnector.ConnectAsync(
            endpoint.Host,
            endpoint.ClientPort,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _loginAsync(connection, endpoint, cancellationToken).ConfigureAwait(false);
            return new TlsClientNodeConnection(endpoint.NodeId, connection, _wireProtocol);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class TlsClientNodeConnection : IClientNodeConnection
{
    private readonly TlsClientConnection _connection;
    private readonly IClientNodeWireProtocol _wireProtocol;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private bool _disposed;

    public int NodeId { get; }

    public TlsClientNodeConnection(
        int nodeId,
        TlsClientConnection connection,
        IClientNodeWireProtocol wireProtocol)
    {
        NodeId = nodeId;
        _connection = connection;
        _wireProtocol = wireProtocol;
    }

    public async Task<ClientNodeStatus> GetClusterStatusAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guid correlationId = Guid.NewGuid();
            ReadOnlyMemory<byte> request = _wireProtocol.CreateGetClusterStatusRequest(datasetId, Guid.NewGuid(), correlationId, NodeId);
            await _connection.Channel.WriteFrameAsync(request, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> response = await _connection.Channel.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            ClientNodeStatus status = _wireProtocol.ParseGetClusterStatusResponse(response, datasetId, NodeId, correlationId);
            if (status.NodeId != NodeId || status.DatasetId != datasetId)
                throw new InvalidDataException("Cluster status does not match the connected endpoint.");
            return status;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<ClientEnvelope> SendAsync(ClientEnvelope request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReadOnlyMemory<byte> frame = _wireProtocol.SerializeEnvelope(request);
            await _connection.Channel.WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> response = await _connection.Channel.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            return _wireProtocol.DeserializeEnvelope(response);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
