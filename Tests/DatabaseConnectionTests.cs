using Coflnet.Core.Security;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using NUnit.Framework;

namespace Coflnet.Sky.PlayerName.Tests;

public class DatabaseConnectionTests
{
    [TestCase(0u, 300u)]
    [TestCase(40u, 40u)]
    [TestCase(900u, 300u)]
    public void TlsPreservesAuthenticationAndOptionsAndBoundsRotation(uint lifetime, uint expectedLifetime)
    {
        var configuration = Configuration(new()
        {
            ["DB:TLS:CA_CERT_PATH"] = "roots.pem", ["DB:TLS:CERT_PATH"] = "client.pem", ["DB:TLS:KEY_PATH"] = "client.key",
            ["DBConnection"] = $"Server=db-a.internal,db-b.internal;Port=3307;User ID=reader;Password=synthetic-only;Database=players;MaximumPoolSize=23;ConnectionTimeout=90;ConnectionLifeTime={lifetime};SslMode=None"
        });
        var options = new MySqlConnectionStringBuilder(TransportSecurity.DatabaseConnectionString(configuration));
        Assert.Multiple(() =>
        {
            Assert.That(options.Server, Is.EqualTo("db-a.internal,db-b.internal"));
            Assert.That(options.Port, Is.EqualTo(3307));
            Assert.That(options.UserID, Is.EqualTo("reader"));
            Assert.That(options.Password, Is.EqualTo("synthetic-only"));
            Assert.That(options.Database, Is.EqualTo("players"));
            Assert.That(options.MaximumPoolSize, Is.EqualTo(23));
            Assert.That(options.ConnectionTimeout, Is.EqualTo(10));
            Assert.That(options.ConnectionLifeTime, Is.EqualTo(expectedLifetime));
            Assert.That(options.SslMode, Is.EqualTo(MySqlSslMode.Required));
            Assert.That(options.SslCa, Is.Empty);
            Assert.That(options.SslCert, Is.Empty);
            Assert.That(options.SslKey, Is.Empty);
        });
    }

    [Test]
    public void MissingTlsConfigurationAndFilesFailClosedAndOptOutPreservesLegacyMode()
    {
        var legacy = new MySqlConnectionStringBuilder(TransportSecurity.DatabaseConnectionString(Configuration(new())));
        Assert.That(legacy.SslMode, Is.EqualTo(MySqlSslMode.None));
        Assert.That(legacy.ConnectionLifeTime, Is.Zero);
        Assert.Throws<InvalidOperationException>(() => TransportSecurity.DatabaseConnectionString(
            Configuration(new() { ["DB:TLS:CERT_PATH"] = "client.pem" })));
        using var owner = new PemTlsOptions("missing-roots", "missing-cert", "missing-key");
        var interceptor = new DatabaseTlsInterceptor(owner);
        using var connection = new MySqlConnection();
        // Missing files prove that logical opens only install callbacks and perform no PKI I/O.
        Assert.DoesNotThrow(() => interceptor.ConnectionOpening(connection, null!, default));
        Assert.That(connection.RemoteCertificateValidationCallback, Is.Not.Null);
        Assert.That(connection.ProvideClientCertificatesCallback, Is.Not.Null);
        Assert.Throws<FileNotFoundException>(() => connection.ProvideClientCertificatesCallback!(new()));
        connection.ProvideClientCertificatesCallback = null;
        connection.RemoteCertificateValidationCallback = null;
        Assert.DoesNotThrow(() => interceptor.ConnectionOpeningAsync(connection, null!, default));
        Assert.That(connection.RemoteCertificateValidationCallback, Is.Not.Null);
        Assert.That(connection.ProvideClientCertificatesCallback, Is.Not.Null);
        Assert.Throws<FileNotFoundException>(() => connection.ProvideClientCertificatesCallback!(new()));
    }

    [Test]
    public async Task RequiredDatabaseStartupCheckFailsBeforeHostReadinessWithoutExposingDsn()
    {
        using var services = new ServiceCollection()
            .AddDbContext<HypixelContext>(options => options.UseMySql(
                "Server=127.0.0.1;Port=1;User ID=reader;Password=synthetic-do-not-log;ConnectionTimeout=1",
                new MariaDbServerVersion(new Version(10, 3))))
            .BuildServiceProvider();
        var check = new DatabaseReadiness(services.GetRequiredService<IServiceScopeFactory>());
        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await check.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(failure!.Message, Is.EqualTo("The required database startup check failed."));
        Assert.That(failure.InnerException, Is.Null);
        await check.StopAsync(CancellationToken.None);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        values.TryAdd("DBConnection", "Server=legacy;User ID=reader;Password=synthetic-only;SslMode=None");
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
