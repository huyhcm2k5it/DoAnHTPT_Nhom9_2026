using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace De6.Contracts;

/// <summary>A node identity proven by a configured certificate pin.</summary>
public sealed class AuthenticatedPeerIdentity
{
    internal AuthenticatedPeerIdentity(int nodeId, string certificateSha256)
    {
        NodeId = nodeId;
        CertificateSha256 = certificateSha256;
    }

    public int NodeId { get; }
    public string CertificateSha256 { get; }
}

/// <summary>Maps the remote certificate to one configured cluster member.</summary>
public sealed class PeerCertificateAuthenticator
{
    private readonly int _localNodeId;
    private readonly IReadOnlyDictionary<string, int> _nodeByCertificateHash;

    public PeerCertificateAuthenticator(int localNodeId, IReadOnlyDictionary<int, string> certificatePins)
    {
        ArgumentNullException.ThrowIfNull(certificatePins);
        if (localNodeId is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(localNodeId));
        if (certificatePins.Count != 3 || !certificatePins.Keys.Order().SequenceEqual(new[] { 1, 2, 3 }))
            throw new ArgumentException("Pins must configure NodeId 1, 2 and 3 exactly once.", nameof(certificatePins));

        var nodeByCertificateHash = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (nodeId, pin) in certificatePins)
        {
            var normalized = NormalizePin(pin, nameof(certificatePins));
            if (!nodeByCertificateHash.TryAdd(normalized, nodeId))
                throw new ArgumentException("Each node must have a distinct certificate pin.", nameof(certificatePins));
        }

        _localNodeId = localNodeId;
        _nodeByCertificateHash = nodeByCertificateHash;
    }

    public AuthenticatedPeerIdentity Authenticate(X509Certificate2 certificate, int claimedNodeId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (claimedNodeId is < 1 or > 3 || claimedNodeId == _localNodeId)
            throw new UnauthorizedAccessException("The claimed sender is not a remote cluster member.");

        var utcNow = now.UtcDateTime;
        if (utcNow < certificate.NotBefore.ToUniversalTime() || utcNow > certificate.NotAfter.ToUniversalTime())
            throw new UnauthorizedAccessException("The peer certificate is outside its validity period.");

        var hash = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
        if (!_nodeByCertificateHash.TryGetValue(hash, out var mappedNodeId) || mappedNodeId != claimedNodeId)
            throw new UnauthorizedAccessException("The peer certificate does not match the claimed NodeId.");

        return new AuthenticatedPeerIdentity(mappedNodeId, hash);
    }

    public AuthenticatedPeerIdentity Authenticate(X509Certificate2 certificate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var hash = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
        if (!_nodeByCertificateHash.TryGetValue(hash, out var nodeId))
            throw new UnauthorizedAccessException("The certificate does not belong to a configured peer.");
        return Authenticate(certificate, nodeId, now);
    }

    private static string NormalizePin(string pin, string parameterName)
    {
        if (pin is null) throw new ArgumentNullException(parameterName);
        var normalized = pin.Replace(":", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Certificate pins must be 64 hexadecimal SHA-256 characters.", parameterName);
        return normalized;
    }
}
