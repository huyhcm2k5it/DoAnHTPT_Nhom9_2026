
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace De6.Transport.Tcp;

/// <summary>
/// </summary>
public static class TlsClientConnector
{
    public static async Task<TlsClientConnection> ConnectAsync(
        string host,
        int port,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        byte[] expectedFingerprint;

        try
        {
            var normalized = expectedSha256
                .Replace(":", "")
                .Replace(" ", "");

            expectedFingerprint = Convert.FromHexString(normalized);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "SHA-256 fingerprint must be hexadecimal.",
                nameof(expectedSha256),
                exception);
        }

        if (expectedFingerprint.Length != 32)
        {
            throw new ArgumentException(
                "SHA-256 fingerprint must contain exactly 32 bytes.",
                nameof(expectedSha256));
        }

        var tcp = new TcpClient();
        SslStream? ssl = null;

        try
        {
            await tcp.ConnectAsync(
                host,
                port,
                cancellationToken);

            ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback:
                    (_, certificate, _, errors) =>
                    {
                        return VerifyServerCertificate(
                            certificate,
                            errors,
                            expectedFingerprint);
                    });

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,

                EnabledSslProtocols =
                    SslProtocols.Tls12 |
                    SslProtocols.Tls13,

                CertificateRevocationCheckMode =
                    X509RevocationMode.NoCheck
            };

            await ssl.AuthenticateAsClientAsync(
                options,
                cancellationToken);

            return new TlsClientConnection(tcp, ssl);
        }
        catch
        {
            if (ssl is not null)
                await ssl.DisposeAsync();

            tcp.Dispose();
            throw;
        }
    }

    private static bool VerifyServerCertificate(
        X509Certificate? certificate,
        SslPolicyErrors errors,
        byte[] expectedFingerprint)
    {
        if (certificate is null)
            return false;

        // Luôn từ chối nếu tên máy chủ không khớp.
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            return false;

        // Từ chối nếu không nhận được certificate.
        if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            return false;

        if (certificate is not X509Certificate2 cert)
            return false;

        var now = DateTime.UtcNow;

        // Kiểm tra thời hạn chứng thư.
        if (now < cert.NotBefore.ToUniversalTime() ||
            now > cert.NotAfter.ToUniversalTime())
        {
            return false;
        }

        // Tính SHA-256 fingerprint.
        byte[] actualFingerprint =
            SHA256.HashData(cert.RawData);

        // So sánh constant-time.
        return CryptographicOperations.FixedTimeEquals(
            actualFingerprint,
            expectedFingerprint);
    }
}

/// <summary>
/// Quản lý vòng đời TCP + TLS + FrameChannel.
/// </summary>
public sealed class TlsClientConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly SslStream _ssl;
    private readonly StreamFrameChannel _channel;

    private int _disposed;

    internal TlsClientConnection(
        TcpClient tcp,
        SslStream ssl)
    {
        _tcp = tcp;
        _ssl = ssl;

        // SslStream đã xác thực trước khi được đưa vào channel.
        _channel = new StreamFrameChannel(ssl);
    }

    /// <summary>
    /// Kênh truyền frame JSON đã được TLS bảo vệ.
    /// </summary>
    public IFrameChannel Channel => _channel;

    public bool IsAuthenticated =>
        Volatile.Read(ref _disposed) == 0 &&
        _ssl.IsAuthenticated &&
        _ssl.IsEncrypted;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _channel.DisposeAsync();

        _tcp.Dispose();
    }
}
