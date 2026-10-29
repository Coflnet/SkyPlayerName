using System;
using System.Data.Common;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Core.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Coflnet.Sky.PlayerName.Infrastructure;

internal static class TransportSecurity
{
    public static string DatabaseConnectionString(IConfiguration configuration)
    {
        var options = new MySqlConnectionStringBuilder(configuration["DBConnection"]
            ?? throw new InvalidOperationException("DBConnection must be configured."));
        options.ConnectionTimeout = options.ConnectionTimeout == 0 ? 10 : Math.Min(options.ConnectionTimeout, 10);
        var section = configuration.GetSection("DB:TLS");
        if (section.Exists())
        {
            options.ConnectionLifeTime = options.ConnectionLifeTime == 0 ? 300 : Math.Min(options.ConnectionLifeTime, 300);
            // MySqlConnector 2.6.2 can accept VerifyFull errors through MariaDB password-bound TLS.
            // Required + an unconditional strict callback rejects bad CA/name before authentication.
            options.SslMode = MySqlSslMode.Required;
            Required(section, "CA_CERT_PATH");
            options.SslCa = "";
            Required(section, "CERT_PATH");
            Required(section, "KEY_PATH");
            // Native PEM settings override the provider, which must run only for physical TLS opens.
            options.SslCert = "";
            options.SslKey = "";
        }
        return options.ConnectionString;
    }

    private static string Required(IConfiguration section, string key) =>
        !string.IsNullOrWhiteSpace(section[key]) ? section[key] :
        throw new InvalidOperationException($"DB:TLS:{key} must be configured.");

    public static void ConfigureHttps(this IWebHostBuilder builder) => builder.ConfigureKestrel((context, options) =>
    {
        var section = context.Configuration.GetSection("HTTPS:TLS");
        if (!section.Exists()) return;
        options.ListenAnyIP(8000);
        options.ListenAnyIP(8443, listen => listen.UseHttps(new TlsHandshakeCallbackOptions
        {
            OnConnection = _ => new ValueTask<SslServerAuthenticationOptions>(CreateServerAuthenticationOptions(
                options.ApplicationServices.GetRequiredKeyedService<PemTlsOptions>("HTTPS"),
                section.GetValue<bool>("REQUIRE_CLIENT_CERTIFICATE")))
        }));
    });

    internal static SslServerAuthenticationOptions CreateServerAuthenticationOptions(PemTlsOptions owner, bool requireClientCertificate = false) => new()
    {
        ServerCertificateContext = owner.GetCertificateContext(),
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        ClientCertificateRequired = true,
        RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            certificate is null ? !requireClientCertificate : owner.ValidateClientCertificate(sender, certificate, chain, errors)
    };
}

internal sealed class DatabaseTlsInterceptor : DbConnectionInterceptor
{
    private readonly PemTlsOptions credentials;
    public DatabaseTlsInterceptor(PemTlsOptions credentials) => this.credentials = credentials;

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Prepare(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Prepare(connection);
        return new ValueTask<InterceptionResult>(result);
    }

    private void Prepare(DbConnection connection)
    {
        var mysql = (MySqlConnection)connection;
        mysql.RemoteCertificateValidationCallback = credentials.ValidateServerCertificate;
        mysql.ProvideClientCertificatesCallback = certificates =>
        {
            credentials.RegisterClientIntermediates();
            certificates.Add(credentials.GetCertificate());
            return ValueTask.CompletedTask;
        };
    }
}
