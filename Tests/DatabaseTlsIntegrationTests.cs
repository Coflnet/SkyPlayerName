using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Coflnet.Core.Security;
using Coflnet.Sky.PlayerName.Infrastructure;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using NUnit.Framework;

namespace Coflnet.Sky.PlayerName.Tests;

[Category("Integration")]
[Platform("Linux")]
public class DatabaseTlsIntegrationTests
{
    // Official mariadb:11.8.8, isolated from the host's .NET intermediate store.
    private const string Image = "mariadb@sha256:d9f7eb2637296652f24b484afd5d246f759f49f5babcadc6a9e344c9acb75fbf";

    [Test]
    public async Task ActualMysqlDriverSendsIntermediateChainAndReloadsPemOnFreshConnections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skyplayername-db-tls-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var container = "skyplayername-tls-test-" + Guid.NewGuid().ToString("N");
        using var root = Root();
        using var nextRoot = Root();
        using var serverCa = Issue(root, authority: true);
        using var clientCa = Issue(root, authority: true);
        using var nextClientCa = Issue(nextRoot, authority: true);
        using var server = Issue(serverCa, server: true);
        using var client = Issue(clientCa);
        using var renewed = Issue(nextClientCa);
        Write(directory, "server", server, serverCa);
        Write(directory, "client", client, clientCa);
        var roots = root.ExportCertificatePem() + "\n" + nextRoot.ExportCertificatePem();
        File.WriteAllText(Path.Combine(directory, "roots.pem"), roots);
        File.WriteAllText(Path.Combine(directory, "server-trust.pem"), roots);
        File.WriteAllText(Path.Combine(directory, "init.sql"), "ALTER USER 'root'@'%' REQUIRE X509;\n");
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        bool started = false;
        try
        {
            await Docker("run", "--detach", "--rm", "--name", container, "--memory", "512m", "--cpus", "2",
                "--publish", $"127.0.0.1:{port}:3306", "--env", "MARIADB_ROOT_PASSWORD=wire-test-only", "--env", "MARIADB_ROOT_HOST=%",
                "--mount", $"type=bind,src={directory},dst=/tls,readonly",
                "--mount", $"type=bind,src={directory}/init.sql,dst=/docker-entrypoint-initdb.d/tls.sql,readonly",
                Image, "--ssl-ca=/tls/server-trust.pem", "--ssl-cert=/tls/server.pem", "--ssl-key=/tls/server.key", "--require-secure-transport=ON");
            started = true;
            await WaitUntilReady(container);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DBConnection"] = $"Server=127.0.0.1;Port={port};User ID=root;Password=wire-test-only;Pooling=false",
                ["DB:TLS:CA_CERT_PATH"] = Path.Combine(directory, "roots.pem"),
                ["DB:TLS:CERT_PATH"] = Path.Combine(directory, "client.pem"),
                ["DB:TLS:KEY_PATH"] = Path.Combine(directory, "client.key")
            }).Build();
            using var owner = PemTlsOptions.FromConfiguration(configuration.GetSection("DB:TLS"));
            var interceptor = new DatabaseTlsInterceptor(owner);
            var connectionString = TransportSecurity.DatabaseConnectionString(configuration);
            async Task<MySqlConnection> Connect(string? overrideConnectionString = null)
            {
                var connection = new MySqlConnection(overrideConnectionString ?? connectionString);
                try
                {
                    await interceptor.ConnectionOpeningAsync(connection, null!, default);
                    await connection.OpenAsync();
                    return connection;
                }
                catch { await connection.DisposeAsync(); throw; }
            }
            await using var original = await Connect();
            Assert.That(await new MySqlCommand("SELECT 1", original).ExecuteScalarAsync(), Is.EqualTo(1));
            // The native server requires X509 and only knows the roots; its own issuer differs from the client's.
            Write(directory, "client", renewed, nextClientCa);
            File.WriteAllText(Path.Combine(directory, "server-trust.pem"), nextRoot.ExportCertificatePem());
            await new MySqlCommand("FLUSH SSL", original).ExecuteNonQueryAsync();
            await using (var fresh = await Connect())
                Assert.That(await new MySqlCommand("SELECT 1", fresh).ExecuteScalarAsync(), Is.EqualTo(1));
            // Old client identity can no longer authenticate to this root-only peer.
            Write(directory, "client", client, clientCa);
            Assert.ThrowsAsync<MySqlException>(async () => { await using var rejected = await Connect(); });
            Write(directory, "client", renewed, nextClientCa);
            var wrongName = new MySqlConnectionStringBuilder(connectionString) { Server = "localhost" };
            Assert.ThrowsAsync<MySqlException>(async () => { await using var rejected = await Connect(wrongName.ConnectionString); });
            File.WriteAllText(Path.Combine(directory, "roots.pem"), nextRoot.ExportCertificatePem());
            Assert.ThrowsAsync<MySqlException>(async () => { await using var rejected = await Connect(); });
            File.WriteAllText(Path.Combine(directory, "roots.pem"), roots);
            using (var wrongKey = client.GetRSAPrivateKey()!)
                File.WriteAllText(Path.Combine(directory, "client.key"), wrongKey.ExportPkcs8PrivateKeyPem());
            Assert.ThrowsAsync<CryptographicException>(async () => { await using var rejected = await Connect(); });
        }
        finally
        {
            if (started) await Docker("stop", "--timeout", "10", container);
            Directory.Delete(directory, true);
        }
    }

    private static async Task<string> Docker(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        Assert.That(process.ExitCode, Is.Zero, await error);
        return await output;
    }

    private static async Task WaitUntilReady(string container)
    {
        using var process = Process.Start(new ProcessStartInfo("docker")
        {
            ArgumentList = { "logs", "--follow", container }, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        async Task Watch(StreamReader reader)
        {
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
                if (line.Contains("port: 3306")) return;
            throw new InvalidOperationException("MariaDB exited before its ready event.");
        }
        var stdout = Watch(process.StandardOutput);
        var stderr = Watch(process.StandardError);
        try { await await Task.WhenAny(stdout, stderr); }
        finally
        {
            timeout.Cancel();
            process.Kill();
            await process.WaitForExitAsync();
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }

    private static void Write(string directory, string name, X509Certificate2 certificate, X509Certificate2 issuer)
    {
        File.WriteAllText(Path.Combine(directory, name + ".pem"), certificate.ExportCertificatePem() + "\n" + issuer.ExportCertificatePem());
        using var key = certificate.GetRSAPrivateKey()!;
        File.WriteAllText(Path.Combine(directory, name + ".key"), key.ExportPkcs8PrivateKeyPem());
    }

    private static X509Certificate2 Root()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=root-" + Guid.NewGuid(), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
    }

    private static X509Certificate2 Issue(X509Certificate2 issuer, bool authority = false, bool server = false)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=leaf-" + Guid.NewGuid(), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(authority, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(authority ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
        if (!authority)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
                { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
        }
        using var certificate = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(authority ? 4 : 2), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }
}
