using System.Security.Cryptography;
using De6.Client.Sdk;

namespace De6.Client.Simulation;

/// <summary>FAKE login for the demo only. The real login protocol is not implemented in this repository snapshot.</summary>
public static class DemoAuthenticator
{
    public const string DemoPassword = "demo";

    public static ClientSession Login(string username, string password, Guid datasetId)
    {
        string owner = (username ?? "").Trim().ToUpperInvariant();
        if (owner is not ("A" or "B" or "C") || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(password ?? ""), System.Text.Encoding.UTF8.GetBytes(DemoPassword)))
            throw new UnauthorizedAccessException("Sai tài khoản hoặc mật khẩu quầy.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ClientSession(owner, datasetId, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), now, now.AddHours(8));
    }
}
