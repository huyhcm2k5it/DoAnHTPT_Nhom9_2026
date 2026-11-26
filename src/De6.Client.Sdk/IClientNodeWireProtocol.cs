using De6.Contracts;

namespace De6.Client.Sdk;

public interface IClientNodeWireProtocol
{
    ReadOnlyMemory<byte> CreateGetClusterStatusRequest(Guid datasetId, Guid messageId, Guid correlationId, int clientId);
    ClientNodeStatus ParseGetClusterStatusResponse(ReadOnlyMemory<byte> responseFrame, Guid datasetId, int expectedNodeId, Guid expectedCorrelationId);
    ReadOnlyMemory<byte> SerializeEnvelope(ClientEnvelope envelope);
    ClientEnvelope DeserializeEnvelope(ReadOnlyMemory<byte> responseFrame);
}
