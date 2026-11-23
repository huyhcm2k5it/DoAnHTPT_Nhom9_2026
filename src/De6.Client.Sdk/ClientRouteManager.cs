using De6.Contracts;
using System.Text.Json;

namespace De6.Client.Sdk;

public sealed class ClientRouteManager : IAsyncDisposable
{
    private readonly ClientOptions _options;
    private readonly IClientNodeConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private IClientNodeConnection? _activeConnection;
    private ClientNodeStatus? _activeStatus;
    private long _generation;
    private int _nextEndpointIndex;
    private bool _disposed;

    public ClientRouteManager(ClientOptions options, IClientNodeConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        IReadOnlyList<string> errors = ConfigurationValidator.Validate(options);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(options));
        _options = options;
        _connectionFactory = connectionFactory;
    }

    public ClientNodeStatus? ActiveStatus
    {
        get
        {
            lock (_stateGate)
                return _activeStatus;
        }
    }

    public async Task<ClientNodeStatus> RefreshPrimaryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<IClientNodeConnection> candidates = [];
        IClientNodeConnection? selectedConnection = null;
        try
        {
            IClientNodeConnection? previous;
            lock (_stateGate)
            {
                previous = _activeConnection;
                _activeConnection = null;
                _activeStatus = null;
                _generation++;
            }
            if (previous is not null)
                await SafeDisposeAsync(previous).ConfigureAwait(false);

            NodeEndpoint[] endpoints = RotateEndpoints(_options.Servers, _nextEndpointIndex);
            List<(IClientNodeConnection Connection, ClientNodeStatus Status)> primaryCandidates = [];
            foreach (NodeEndpoint endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IClientNodeConnection? connection = null;
                try
                {
                    connection = await _connectionFactory.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    candidates.Add(connection);
                    if (connection.NodeId != endpoint.NodeId)
                        continue;
                    ClientNodeStatus status = await connection.GetClusterStatusAsync(_options.DatasetId, cancellationToken).ConfigureAwait(false);
                    if (status.NodeId != endpoint.NodeId || status.DatasetId != _options.DatasetId)
                        continue;
                    if (status.Epoch < 1 || !status.SqlHealthy || status.StableIndex < 0 ||
                        status.LocalAppliedIndex < status.StableIndex || status.FollowerLag < 0)
                        continue;
                    if (status.Role == ClientNodeRole.PrimaryReady && status.AcceptedLeaderId == status.NodeId)
                        primaryCandidates.Add((connection, status));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    if (connection is not null)
                    {
                        candidates.Remove(connection);
                        await SafeDisposeAsync(connection).ConfigureAwait(false);
                    }
                }
            }

            if (primaryCandidates.Count > 1)
                throw new ClientRouteException("More than one endpoint reports PRIMARY_READY. The SDK refuses to route while cluster authority is ambiguous.");
            if (primaryCandidates.Count == 0)
                throw new ClientRouteException("No configured endpoint reports a valid PRIMARY_READY status.");

            var selected = primaryCandidates[0];
            selectedConnection = selected.Connection;
            candidates.Remove(selected.Connection);
            foreach (IClientNodeConnection candidate in candidates)
                await SafeDisposeAsync(candidate).ConfigureAwait(false);
            candidates.Clear();
            lock (_stateGate)
            {
                _activeConnection = selected.Connection;
                _activeStatus = selected.Status;
                _generation++;
                _nextEndpointIndex = Array.FindIndex(_options.Servers, x => x.NodeId == selected.Status.NodeId) + 1;
                if (_nextEndpointIndex >= _options.Servers.Length)
                    _nextEndpointIndex = 0;
            }
            return selected.Status;
        }
        catch
        {
            if (selectedConnection is not null)
                await SafeDisposeAsync(selectedConnection).ConfigureAwait(false);
            foreach (IClientNodeConnection candidate in candidates)
                await SafeDisposeAsync(candidate).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<ClientEnvelope> SendAsync(ClientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException("Request payload must be a JSON value.", nameof(request));

        IClientNodeConnection? connection;
        long generation;
        lock (_stateGate)
        {
            connection = _activeConnection;
            generation = _generation;
        }
        if (connection is null)
        {
            await RefreshPrimaryAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                connection = _activeConnection;
                generation = _generation;
            }
        }
        if (connection is null)
            throw new ClientRouteException("No active PRIMARY_READY connection is available.");

        IClientNodeConnection checkedConnection = connection;
        try
        {
            ClientNodeStatus currentStatus = await checkedConnection.GetClusterStatusAsync(_options.DatasetId, cancellationToken).ConfigureAwait(false);
            bool authorityChanged;
            lock (_stateGate)
            {
                authorityChanged = ReferenceEquals(_activeConnection, checkedConnection) &&
                    _activeStatus is not null &&
                    (_activeStatus.Epoch != currentStatus.Epoch ||
                     _activeStatus.AcceptedLeaderId != currentStatus.AcceptedLeaderId);
                if (ReferenceEquals(_activeConnection, checkedConnection) && !authorityChanged)
                    _activeStatus = currentStatus;
            }
            if (currentStatus.NodeId != checkedConnection.NodeId || currentStatus.DatasetId != _options.DatasetId ||
                currentStatus.Role != ClientNodeRole.PrimaryReady || currentStatus.AcceptedLeaderId != currentStatus.NodeId ||
                currentStatus.Epoch < 1 || !currentStatus.SqlHealthy || currentStatus.StableIndex < 0 ||
                currentStatus.LocalAppliedIndex < currentStatus.StableIndex || currentStatus.FollowerLag < 0 || authorityChanged)
            {
                await InvalidateAndReconnectAsync(checkedConnection, cancellationToken).ConfigureAwait(false);
                lock (_stateGate)
                {
                    connection = _activeConnection;
                    generation = _generation;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await InvalidateAndReconnectAsync(checkedConnection, cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                connection = _activeConnection;
                generation = _generation;
            }
        }
        lock (_stateGate)
        {
            if (!ReferenceEquals(_activeConnection, connection) || _generation != generation)
            {
                connection = _activeConnection;
                generation = _generation;
            }
        }
        if (connection is null)
        {
            await RefreshPrimaryAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                connection = _activeConnection;
                generation = _generation;
            }
        }
        if (connection is null)
            throw new ClientRouteException("No valid PRIMARY_READY connection is available after route refresh.");

        Guid correlationId = Guid.NewGuid();
        ClientEnvelope outbound = new(
            1,
            _options.DatasetId,
            Guid.NewGuid(),
            correlationId,
            _options.ClientId,
            request.Payload,
            request.RequestId);

        try
        {
            ClientEnvelope response = await connection.SendAsync(outbound, cancellationToken).ConfigureAwait(false);
            if (response.ProtocolVersion != 1 || response.DatasetId != _options.DatasetId ||
                response.SenderId != connection.NodeId || response.MessageId == Guid.Empty || response.CorrelationId != correlationId)
                throw new InvalidDataException("Response envelope does not match the active request.");

            lock (_stateGate)
            {
                if (!ReferenceEquals(_activeConnection, connection) || _generation != generation)
                    throw new StaleClientResponseException(request.RequestId, correlationId);
            }
            return response;
        }
        catch (StaleClientResponseException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await InvalidateAndReconnectAsync(connection, CancellationToken.None).ConfigureAwait(false);
            throw new ClientTransportUncertainException(request.RequestId, correlationId, exception);
        }
        catch (Exception exception)
        {
            await InvalidateAndReconnectAsync(connection, CancellationToken.None).ConfigureAwait(false);
            throw new ClientTransportUncertainException(request.RequestId, correlationId, exception);
        }
    }

    public async Task InvalidateAndReconnectAsync(CancellationToken cancellationToken = default)
    {
        IClientNodeConnection? connection;
        lock (_stateGate)
        {
            connection = _activeConnection;
            _activeConnection = null;
            _activeStatus = null;
            _generation++;
        }
        if (connection is not null)
            await SafeDisposeAsync(connection).ConfigureAwait(false);
        await RefreshPrimaryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InvalidateAndReconnectAsync(IClientNodeConnection failedConnection, CancellationToken cancellationToken)
    {
        bool wasActive;
        lock (_stateGate)
        {
            wasActive = ReferenceEquals(_activeConnection, failedConnection);
            if (wasActive)
            {
                _activeConnection = null;
                _activeStatus = null;
                _generation++;
            }
        }
        if (wasActive)
        {
            await SafeDisposeAsync(failedConnection).ConfigureAwait(false);
            try
            {
                await RefreshPrimaryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    private static NodeEndpoint[] RotateEndpoints(NodeEndpoint[] endpoints, int startIndex)
    {
        if (endpoints.Length == 0)
            return [];
        int start = ((startIndex % endpoints.Length) + endpoints.Length) % endpoints.Length;
        return Enumerable.Range(0, endpoints.Length)
            .Select(offset => endpoints[(start + offset) % endpoints.Length])
            .ToArray();
    }

    private static async ValueTask SafeDisposeAsync(IClientNodeConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
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
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            IClientNodeConnection? connection;
            lock (_stateGate)
            {
                connection = _activeConnection;
                _activeConnection = null;
                _activeStatus = null;
                _generation++;
            }
            if (connection is not null)
                await SafeDisposeAsync(connection).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
            _refreshGate.Dispose();
        }
    }
}
