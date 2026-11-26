namespace De6.Client.Sdk;

public sealed class ClientSession
{
    public string OwnerId { get; }
    public Guid DatasetId { get; }
    public DateTimeOffset AuthenticatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string AccessToken { get; }

    public ClientSession(string ownerId, Guid datasetId, string accessToken, DateTimeOffset authenticatedAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (datasetId == Guid.Empty)
            throw new ArgumentException("DatasetId cannot be empty.", nameof(datasetId));
        if (expiresAt <= authenticatedAt)
            throw new ArgumentException("Session expiry must be later than authentication time.", nameof(expiresAt));
        OwnerId = ownerId;
        DatasetId = datasetId;
        AccessToken = accessToken;
        AuthenticatedAt = authenticatedAt;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired(DateTimeOffset? now = null) => (now ?? DateTimeOffset.UtcNow) >= ExpiresAt;
}
