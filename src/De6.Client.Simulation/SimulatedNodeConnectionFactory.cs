using System.Text.Json;
using De6.Client.Sdk;
using De6.Contracts;

namespace De6.Client.Simulation;

/// <summary>FAKE S1-S3 as seen through the client SDK connection abstraction.</summary>
public sealed class SimulatedNodeConnectionFactory : IClientNodeConnectionFactory
{
    private readonly SimulatedCluster _cluster;
    private readonly string _sessionOwnerId;
    private readonly SimulatedFaults _faults;
    private readonly int _latencyMs;

    public SimulatedNodeConnectionFactory(SimulatedCluster cluster, string sessionOwnerId, SimulatedFaults faults, int latencyMs = 60)
    {
        _cluster = cluster; _sessionOwnerId = sessionOwnerId; _faults = faults; _latencyMs = latencyMs;
    }

    public async Task<IClientNodeConnection> ConnectAsync(NodeEndpoint endpoint, CancellationToken cancellationToken)
    {
        await Task.Delay(_latencyMs, cancellationToken).ConfigureAwait(false);
        if (_faults.NetworkDown) throw new IOException("Simulated network is down.");
        return new Connection(endpoint.NodeId, _cluster, _sessionOwnerId, _faults, _latencyMs);
    }

    private sealed class Connection : IClientNodeConnection
    {
        private readonly SimulatedCluster _cluster;
        private readonly string _owner;
        private readonly SimulatedFaults _faults;
        private readonly int _latencyMs;
        public int NodeId { get; }

        public Connection(int nodeId, SimulatedCluster cluster, string owner, SimulatedFaults faults, int latencyMs)
        { NodeId = nodeId; _cluster = cluster; _owner = owner; _faults = faults; _latencyMs = latencyMs; }

        public async Task<ClientNodeStatus> GetClusterStatusAsync(Guid datasetId, CancellationToken cancellationToken)
        {
            await Task.Delay(_latencyMs / 2, cancellationToken).ConfigureAwait(false);
            if (_faults.NetworkDown) throw new IOException("Simulated network is down.");
            long applied = _cluster.AppliedIndex;
            bool primary = NodeId == SimulatedCluster.PrimaryNodeId;
            return new ClientNodeStatus(NodeId, datasetId, primary ? ClientNodeRole.PrimaryReady : ClientNodeRole.BackupReady,
                _cluster.Epoch, SimulatedCluster.PrimaryNodeId, true, applied, applied, 0);
        }

        public async Task<ClientEnvelope> SendAsync(ClientEnvelope request, CancellationToken cancellationToken)
        {
            await Task.Delay(_latencyMs, cancellationToken).ConfigureAwait(false);
            if (_faults.NetworkDown) throw new IOException("Simulated network is down.");
            JsonElement p = request.Payload;
            string? type = p.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
            string body;
            if (type == "GET_RESULT")
            {
                BusinessResult? r = request.RequestId is { } id ? _cluster.GetResult(_owner, id) : null;
                body = r is null ? "{\"status\":\"NOT_FOUND\"}" : $"{{\"status\":\"FINAL\",\"result\":{System.Text.Encoding.UTF8.GetString(CanonicalProtocol.SerializeBusinessResult(r))}}}";
            }
            else if (type == "GET_SEATS")
                body = $"{{\"status\":\"FINAL\",\"result\":{System.Text.Encoding.UTF8.GetString(ReadModelCodec.WriteSeats(_cluster.ReadSeats()))}}}";
            else if (type == "GET_MY_RESERVATIONS")
                body = $"{{\"status\":\"FINAL\",\"result\":{System.Text.Encoding.UTF8.GetString(ReadModelCodec.WriteReservations(_cluster.ReadReservations(_owner)))}}}";
            else
            {
                // OwnerId is taken from the session, never from the JSON payload (V12 section 7).
                if (request.RequestId is not { } requestId || !p.TryGetProperty("action", out JsonElement a))
                    body = "{\"status\":\"REJECTED\"}";
                else
                {
                    BookingAction action = CanonicalProtocol.ParseAction(a.GetString()!);
                    Guid? reservation = p.TryGetProperty("reservationId", out JsonElement r) && r.ValueKind == JsonValueKind.String ? Guid.Parse(r.GetString()!) : null;
                    CommandData command = new(action, p.GetProperty("tripId").GetInt32(), p.GetProperty("seatNo").GetInt32(), reservation);
                    string hash = CanonicalProtocol.ComputeCommandHashString(request.DatasetId, _owner, action, command.TripId, command.SeatNo, reservation);
                    BusinessResult result = _cluster.Execute(_owner, requestId, hash, command);
                    bool cut = _faults.TakeCut();
                    if (cut) _faults.NetworkDown = true;
                    if (cut || _faults.TakeDrop()) throw new IOException("Simulated response lost after the cluster applied the command.");
                    body = $"{{\"status\":\"FINAL\",\"result\":{System.Text.Encoding.UTF8.GetString(CanonicalProtocol.SerializeBusinessResult(result))}}}";
                }
            }
            using JsonDocument document = JsonDocument.Parse(body);
            return new ClientEnvelope(1, request.DatasetId, Guid.NewGuid(), request.CorrelationId, NodeId, document.RootElement.Clone(), request.RequestId);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
