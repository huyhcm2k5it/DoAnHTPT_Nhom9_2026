namespace De6.Client.Sdk;

public sealed record ClientLoginRequest(string Username, string Password, Guid DatasetId);

public interface IClientLoginProtocol
{
    ReadOnlyMemory<byte> CreateLoginRequest(ClientLoginRequest request, Guid messageId, Guid correlationId);
    ClientSession ParseLoginResponse(ReadOnlyMemory<byte> responseFrame, Guid expectedDatasetId, Guid expectedCorrelationId);
}
