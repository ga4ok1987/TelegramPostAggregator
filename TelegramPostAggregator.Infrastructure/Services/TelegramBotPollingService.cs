using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramBotPollingService(
    ITelegramBotGateway telegramBotGateway,
    IErrorAlertService errorAlertService,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotPollingService> logger) : BackgroundService
{
    private readonly TelegramBotOptions _options = options.Value;
    private long _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Telegram bot polling is disabled because BotToken is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await telegramBotGateway.GetUpdatesAsync(_offset, stoppingToken);
                foreach (var update in updates)
                {
                    _offset = update.UpdateId + 1;
                    await ProcessUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram bot polling iteration failed.");
                await errorAlertService.SendAsync(
                    "Bot polling iteration failed",
                    "Telegram bot polling loop failed.",
                    exception,
                    stoppingToken);
                await Task.Delay(_options.PollingDelayMilliseconds, stoppingToken);
            }
        }
    }

    private async Task ProcessUpdateAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken)
    {
        if (update.ChatId is null)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IBotUpdateProcessor>();
        var result = await processor.ProcessAsync(update, cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            await telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(update.ChatId.Value, result.Message, ReplyMarkup: result.ReplyMarkup), cancellationToken);
        }
    }
}
