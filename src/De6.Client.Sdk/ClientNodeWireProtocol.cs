using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public sealed class ClientNodeWireProtocol : IClientNodeWireProtocol
{
    public ReadOnlyMemory<byte> CreateGetClusterStatusRequest(Guid datasetId, Guid messageId, Guid correlationId, int clientId)
    {
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));

        using var doc = JsonDocument.Parse("{\"type\":\"GET_CLUSTER_STATUS\"}");
        var envelope = new ClientEnvelope(
            1,
            datasetId,
            messageId == Guid.Empty ? Guid.NewGuid() : messageId,
            correlationId,
            clientId,
            doc.RootElement.Clone());

        return WireCodec.SerializeEnvelope(envelope);
    }

    public ClientNodeStatus ParseGetClusterStatusResponse(ReadOnlyMemory<byte> responseFrame, Guid datasetId, int expectedNodeId, Guid expectedCorrelationId)
    {
        var envelope = WireCodec.DeserializeEnvelope(responseFrame);
        if (envelope.DatasetId != datasetId)
            throw new InvalidDataException($"Cluster status datasetId '{envelope.DatasetId}' does not match expected '{datasetId}'.");
        if (envelope.CorrelationId != expectedCorrelationId)
            throw new InvalidDataException($"Cluster status correlationId '{envelope.CorrelationId}' does not match expected '{expectedCorrelationId}'.");

        var payload = envelope.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cluster status payload must be a JSON object.");

        int nodeId = payload.GetProperty("nodeId").GetInt32();
        if (expectedNodeId > 0 && (nodeId != expectedNodeId || envelope.SenderId != expectedNodeId))
            throw new InvalidDataException($"Cluster status nodeId '{nodeId}' does not match expected '{expectedNodeId}'.");

        string roleStr = payload.GetProperty("role").GetString() ?? "Unknown";
        string normalizedRole = roleStr.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!Enum.TryParse<ClientNodeRole>(normalizedRole, ignoreCase: true, out var role))
            role = ClientNodeRole.Unknown;

        long epoch = payload.GetProperty("epoch").GetInt64();
        int acceptedLeaderId = payload.GetProperty("acceptedLeaderId").GetInt32();
        bool sqlHealthy = payload.GetProperty("sqlHealthy").GetBoolean();
        long localAppliedIndex = payload.GetProperty("localAppliedIndex").GetInt64();
        long stableIndex = payload.GetProperty("stableIndex").GetInt64();
        long followerLag = payload.GetProperty("followerLag").GetInt64();

        return new ClientNodeStatus(nodeId, datasetId, role, epoch, acceptedLeaderId, sqlHealthy, localAppliedIndex, stableIndex, followerLag);
    }

    public ReadOnlyMemory<byte> SerializeEnvelope(ClientEnvelope envelope) => WireCodec.SerializeEnvelope(envelope);

    public ClientEnvelope DeserializeEnvelope(ReadOnlyMemory<byte> responseFrame) => WireCodec.DeserializeEnvelope(responseFrame);
}
