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
                    "Telegram bot polling failed",
                    "Polling loop failed while reading bot updates.",
                    exception,
                    stoppingToken);
                await Task.Delay(_options.PollingDelayMilliseconds, stoppingToken);
            }
        }
    }

    private async Task ProcessUpdateAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IBotUpdateProcessor>();
        var result = await processor.ProcessAsync(update, cancellationToken);

        if (!string.IsNullOrWhiteSpace(update.CallbackQueryId))
        {
            var callbackResponse = await telegramBotGateway.AnswerCallbackQueryAsync(update.CallbackQueryId, result.CallbackNotification, cancellationToken);
            EnsureSuccess(callbackResponse, "answerCallbackQuery");
        }

        if (!string.IsNullOrWhiteSpace(result.Message) && update.ChatId.HasValue)
        {
            var response = await telegramBotGateway.SendMessageAsync(
                new TelegramBotOutboundMessageDto(update.ChatId.Value, result.Message, result.ReplyMarkup),
                cancellationToken);
            EnsureSuccess(response, "sendMessage");
        }
    }

    private static void EnsureSuccess(TelegramBotApiResultDto result, string endpoint)
    {
        if (!result.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Telegram Bot API call {endpoint} failed with {(int)result.StatusCode}: {result.ResponseBody}");
        }
    }
}
