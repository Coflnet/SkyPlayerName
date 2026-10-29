using System;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coflnet.Sky.PlayerName.Infrastructure;

internal sealed class DatabaseReadiness : IHostedService
{
    private readonly IServiceScopeFactory scopes;
    public DatabaseReadiness(IServiceScopeFactory scopes) => this.scopes = scopes;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        try
        {
            await CheckAsync(scope.ServiceProvider.GetRequiredService<HypixelContext>(), cancellationToken, 10);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The required database startup check failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task CheckAsync(HypixelContext database, CancellationToken cancellationToken, int timeoutSeconds = 5)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        await database.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
    }
}
