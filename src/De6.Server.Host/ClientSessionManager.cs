using System.Security.Cryptography;
using De6.Contracts;

namespace De6.Server.Host;

public sealed record ClientAccount(string Username, string OwnerId, string Salt, string PasswordHash);

public sealed record ClientSessionRecord(string Token, string OwnerId, Guid DatasetId, DateTimeOffset AuthenticatedAt, DateTimeOffset ExpiresAt)
{
    public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;
}

public sealed class ClientSessionManager
{
    private readonly Dictionary<string, ClientAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClientSessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ClientSessionManager(IEnumerable<ClientAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        foreach (var acc in accounts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acc.Username);
            BookingCanonical.ValidateOwner(acc.OwnerId);
            if (Convert.FromBase64String(acc.Salt).Length != 16 ||
                Convert.FromBase64String(acc.PasswordHash).Length != 32)
                throw new ArgumentException("Account salt or password hash has an invalid length.", nameof(accounts));
            _accounts.Add(acc.Username, acc);
        }
    }

    private const int PasswordIterations = 600_000;

    public static ClientAccount HashAccount(string username, string ownerId, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return new(username, ownerId, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public ClientSessionRecord? Authenticate(string username, string password, Guid datasetId)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || datasetId == Guid.Empty)
            return null;

        if (!_accounts.TryGetValue(username, out var account))
            return null;

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(account.Salt),
            PasswordIterations, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(account.PasswordHash)))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(8);
        var session = new ClientSessionRecord(token, account.OwnerId, datasetId, now, expires);

        lock (_gate)
        {
            foreach (var expired in _sessions.Values.Where(s => s.IsExpired()).ToArray())
                _sessions.Remove(expired.Token);
            _sessions[token] = session;
        }
        return session;
    }

    public ClientSessionRecord? ValidateSession(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionToken, out var session))
            {
                if (session.IsExpired())
                {
                    _sessions.Remove(sessionToken);
                    return null;
                }
                return session;
            }
        }
        return null;
    }
}
