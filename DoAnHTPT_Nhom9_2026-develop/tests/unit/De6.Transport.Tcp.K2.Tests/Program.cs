
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using De6.Transport.Tcp;

// ==========================================
// K2 TLS CLIENT TESTS
// ==========================================

int passed = 0;
int failed = 0;

// ==========================================
// HÀM CHẠY TEST
// ==========================================

async Task Check(string name, Func<Task<bool>> test)
{
    try
    {
        bool result = await test();

        if (result)
        {
            Console.WriteLine($"[PASS] {name}");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] {name}");
            failed++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");

        Console.WriteLine(ex.StackTrace);
        failed++;
    }
}

// ==========================================
// TẠO CERTIFICATE TEST
// ==========================================

X509Certificate2 CreateCertificate(
    DateTimeOffset notBefore,
    DateTimeOffset notAfter)
{
    using var rsa = RSA.Create(2048);

    var request = new CertificateRequest(
        "CN=localhost",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(
            false,
            false,
            0,
            false));

    request.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.KeyEncipherment,
            false));

    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("localhost");

    request.CertificateExtensions.Add(san.Build());

    var eku = new OidCollection
    {
        new Oid("1.3.6.1.5.5.7.3.1")
    };

    request.CertificateExtensions.Add(
        new X509EnhancedKeyUsageExtension(
            eku,
            false));

    using var generated = request.CreateSelfSigned(
        notBefore,
        notAfter);

    const string password = "K2-Test-Password-2026";

    byte[] pfx = generated.Export(
        X509ContentType.Pfx,
        password);

    return X509CertificateLoader.LoadPkcs12(
        pfx,
        password,
        X509KeyStorageFlags.UserKeySet |
        X509KeyStorageFlags.PersistKeySet |
        X509KeyStorageFlags.Exportable);
}

// ==========================================
// TÍNH SHA256 FINGERPRINT
// ==========================================

string Fingerprint(X509Certificate2 cert)
{
    return Convert.ToHexString(
        SHA256.HashData(cert.RawData));
}

// ==========================================
// TẠO TLS TEST SERVER
// ==========================================

async Task<(int Port, Task<bool> ServerTask)> StartServer(
    X509Certificate2 certificate)
{
    var listener = new TcpListener(
        IPAddress.Loopback,
        0);

    listener.Start();

    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

    var serverTask = Task.Run(async () =>
    {
        bool handshakeSucceeded = false;

        try
        {
            using var tcp =
                await listener.AcceptTcpClientAsync();

            Console.WriteLine("[SERVER] Client connected.");

            await using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false);

            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,

                    EnabledSslProtocols =
                        SslProtocols.Tls12 |
                        SslProtocols.Tls13
                });

            handshakeSucceeded = true;

            Console.WriteLine(
                "[SERVER] TLS handshake successful.");

            try
            {
                await ssl.CopyToAsync(Stream.Null);
            }
            catch (IOException ex)
            {
                Console.WriteLine(
                    $"[SERVER] Connection closed: {ex.Message}");
            }

            return handshakeSucceeded;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SERVER ERROR] {ex.GetType().Name}: {ex.Message}");

            return false;
        }
        finally
        {
            listener.Stop();
        }
    });

    return (port, serverTask);
}

// ==========================================
// KIỂM TRA CLIENT BỊ TỪ CHỐI
// ==========================================

async Task<bool> ExpectRejected(
    string host,
    int port,
    string fingerprint)
{
    try
    {
        await using var connection =
            await TlsClientConnector.ConnectAsync(
                host,
                port,
                fingerprint);

        Console.WriteLine(
            "[CLIENT] Connection unexpectedly succeeded.");

        return false;
    }
    catch (AuthenticationException ex)
    {
        Console.WriteLine(
            $"[CLIENT] Authentication rejected: {ex.Message}");

        return true;
    }
    catch (IOException ex)
    {
        Console.WriteLine(
            $"[CLIENT] TLS connection rejected: {ex.Message}");

        return true;
    }
}

// ==========================================
// T01: CERTIFICATE ĐÚNG FINGERPRINT
// ==========================================

await Check(
    "T01 - Valid certificate and fingerprint",
    async () =>
    {
        using var cert = CreateCertificate(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));

        var server = await StartServer(cert);

        bool clientSuccess = false;

        try
        {
            await using var connection =
                await TlsClientConnector.ConnectAsync(
                    "localhost",
                    server.Port,
                    Fingerprint(cert));

            clientSuccess = connection.IsAuthenticated;

            Console.WriteLine(
                $"[CLIENT] Authenticated: {connection.IsAuthenticated}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[CLIENT ERROR] {ex.GetType().Name}: {ex.Message}");
        }

        bool serverSuccess = await server.ServerTask;

        return clientSuccess && serverSuccess;
    });

// ==========================================
// T02: FINGERPRINT SAI
// ==========================================

await Check(
    "T02 - Wrong fingerprint rejected",
    async () =>
    {
        using var cert = CreateCertificate(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));

        var server = await StartServer(cert);

        bool clientRejected = await ExpectRejected(
            "localhost",
            server.Port,
            new string('0', 64));

        await server.ServerTask;

        return clientRejected;
    });

// ==========================================
// T03: HOSTNAME KHÔNG KHỚP
// ==========================================

await Check(
    "T03 - Hostname mismatch rejected",
    async () =>
    {
        using var cert = CreateCertificate(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));

        var server = await StartServer(cert);

        bool clientRejected = await ExpectRejected(
            "127.0.0.1",
            server.Port,
            Fingerprint(cert));

        await server.ServerTask;

        return clientRejected;
    });

// ==========================================
// T04: CERTIFICATE HẾT HẠN
// ==========================================

await Check(
    "T04 - Expired certificate rejected",
    async () =>
    {
        using var cert = CreateCertificate(
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow.AddDays(-5));

        var server = await StartServer(cert);

        bool clientRejected = await ExpectRejected(
            "localhost",
            server.Port,
            Fingerprint(cert));

        await server.ServerTask;

        return clientRejected;
    });

// ==========================================
// TỔNG KẾT TEST
// ==========================================

Console.WriteLine();

Console.WriteLine("================================");
Console.WriteLine("       K2 TLS TEST RESULTS");
Console.WriteLine("================================");

Console.WriteLine($"PASSED: {passed}");
Console.WriteLine($"FAILED: {failed}");

Console.WriteLine("================================");

if (failed == 0)
{
    Console.WriteLine("ALL TESTS PASSED!");
}
else
{
    Console.WriteLine("SOME TESTS FAILED!");
}

return failed == 0 ? 0 : 1;
