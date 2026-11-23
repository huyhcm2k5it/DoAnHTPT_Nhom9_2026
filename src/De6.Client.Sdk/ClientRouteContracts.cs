using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public interface IClientNodeConnection : IAsyncDisposable
{
    int NodeId { get; }
    Task<ClientNodeStatus> GetClusterStatusAsync(Guid datasetId, CancellationToken cancellationToken);
    Task<ClientEnvelope> SendAsync(ClientEnvelope request, CancellationToken cancellationToken);
}

public interface IClientNodeConnectionFactory
{
    Task<IClientNodeConnection> ConnectAsync(De6.Contracts.NodeEndpoint endpoint, CancellationToken cancellationToken);
}

public sealed class ClientRouteException : Exception
{
    public ClientRouteException(string message) : base(message)
    {
    }
}

public sealed class ClientTransportUncertainException : Exception
{
    public Guid? RequestId { get; }
    public Guid CorrelationId { get; }

    public ClientTransportUncertainException(Guid? requestId, Guid correlationId, Exception innerException)
        : base("The connection was lost while awaiting a response. The request outcome is uncertain.", innerException)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
    }
}

public sealed class StaleClientResponseException : Exception
{
    public Guid? RequestId { get; }
    public Guid CorrelationId { get; }

    public StaleClientResponseException(Guid? requestId, Guid correlationId)
        : base("The response belongs to a connection that is no longer the active route.")
    {
        RequestId = requestId;
        CorrelationId = correlationId;
    }
}
