using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TdLibCollectorHostedService(
    IServiceScopeFactory scopeFactory,
    TdLibCollectorClientManager manager,
    IOptions<TdLibOptions> options,
    ILogger<TdLibCollectorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.UseSimulation)
        {
            logger.LogInformation("TDLib collector initialization is skipped because simulation mode is enabled.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICollectorAccountRepository>();
        var collector = await repository.GetPrimaryAvailableAsync(stoppingToken);
        if (collector is null)
        {
            logger.LogWarning("No active collector account found to initialize TDLib.");
            return;
        }

        try
        {
            await manager.InitializeAsync(collector, stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize TDLib collector runtime.");
        }
    }
}
