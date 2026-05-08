using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services.Bot;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramBotPollingService(
    ITelegramBotGateway telegramBotGateway,
    IErrorAlertService errorAlertService,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    IOptions<Application.Options.MiniAppOptions> miniAppOptions,
    BotLocalizationCatalog localizationCatalog,
    ILogger<TelegramBotPollingService> logger) : BackgroundService
{
    private readonly TelegramBotOptions _options = options.Value;
    private readonly Application.Options.MiniAppOptions _miniAppOptions = miniAppOptions.Value;
    private readonly BotLocalizationCatalog _localizationCatalog = localizationCatalog;
    private static readonly TimeSpan UpdateProcessingTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TransientPollingAlertThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TransientPollingAlertCooldown = TimeSpan.FromMinutes(10);
    private static readonly Regex RetryAfterRegex = new("\"retry_after\"\\s*:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly ConcurrentDictionary<long, PendingBotReply> PendingReplies = new();
    private static readonly ConcurrentDictionary<long, byte> ActiveReplyWorkers = new();
    private long _offset;
    private int _consecutivePollingFailures;
    private DateTimeOffset? _firstPollingFailureAtUtc;
    private DateTimeOffset? _lastTransientPollingAlertAtUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Telegram bot polling is disabled because BotToken is not configured.");
            return;
        }

        await EnsureMiniAppMenuButtonAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await telegramBotGateway.GetUpdatesAsync(_offset, stoppingToken);
                ResetTransientPollingFailuresIfNeeded();
                foreach (var update in updates)
                {
                    _offset = update.UpdateId + 1;
                    await ProcessUpdateWithTimeoutAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TaskCanceledException exception) when (!stoppingToken.IsCancellationRequested)
            {
                await HandleTransientPollingFailureAsync(
                    exception,
                    "Telegram bot long polling timed out while waiting for updates.",
                    TimeSpan.FromSeconds(2),
                    stoppingToken);
            }
            catch (HttpRequestException exception) when (IsTransientPollingStatusCode(exception.StatusCode))
            {
                await HandleTransientPollingFailureAsync(
                    exception,
                    "Telegram bot polling hit a transient Bot API error.",
                    ResolveTransientPollingDelay(exception),
                    stoppingToken);
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

    private async Task HandleTransientPollingFailureAsync(
        Exception exception,
        string logMessage,
        TimeSpan retryDelay,
        CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        _consecutivePollingFailures++;
        _firstPollingFailureAtUtc ??= now;

        logger.LogWarning(
            exception,
            "{LogMessage} ConsecutiveFailures={ConsecutiveFailures}, RetryDelaySeconds={RetryDelaySeconds}",
            logMessage,
            _consecutivePollingFailures,
            retryDelay.TotalSeconds);

        if (ShouldAlertOnTransientPollingFailure(now))
        {
            _lastTransientPollingAlertAtUtc = now;
            await errorAlertService.SendAsync(
                "Telegram bot polling degraded",
                $"Polling is encountering transient Bot API errors.\nConsecutiveFailures: {_consecutivePollingFailures}\nFirstFailureAtUtc: {_firstPollingFailureAtUtc:O}\nNextRetryDelaySeconds: {Math.Max(1, (int)Math.Ceiling(retryDelay.TotalSeconds))}",
                exception,
                stoppingToken);
        }

        await Task.Delay(retryDelay, stoppingToken);
    }

    private async Task EnsureMiniAppMenuButtonAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_miniAppOptions.Url))
        {
            return;
        }

        var response = await telegramBotGateway.SetChatMenuButtonAsync(
            _localizationCatalog.MiniAppButtonLabel,
            _miniAppOptions.Url,
            cancellationToken);

        await EnsureSuccessAsync(response, "setChatMenuButton", cancellationToken);
    }

    private async Task ProcessUpdateAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing bot update. UpdateId={UpdateId}, ChatId={ChatId}, HasText={HasText}, Text={Text}, CallbackData={CallbackData}, SharedChatId={SharedChatId}",
            update.UpdateId,
            update.ChatId,
            !string.IsNullOrWhiteSpace(update.Text),
            string.IsNullOrWhiteSpace(update.Text) ? "(null)" : update.Text,
            update.CallbackData ?? "(null)",
            update.SharedChat?.ChatId);

        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IBotUpdateProcessor>();
        var result = await processor.ProcessAsync(update, cancellationToken);

        if (!string.IsNullOrWhiteSpace(update.CallbackQueryId))
        {
            var callbackResponse = await telegramBotGateway.AnswerCallbackQueryAsync(update.CallbackQueryId, result.CallbackNotification, cancellationToken);
            if (!await EnsureSuccessAsync(callbackResponse, "answerCallbackQuery", cancellationToken))
            {
                return;
            }
        }

        if (result.PreCheckoutResponse is not null)
        {
            var preCheckoutResponse = await telegramBotGateway.AnswerPreCheckoutQueryAsync(
                result.PreCheckoutResponse.PreCheckoutQueryId,
                result.PreCheckoutResponse.Ok,
                result.PreCheckoutResponse.ErrorMessage,
                cancellationToken);
            if (!await EnsureSuccessAsync(preCheckoutResponse, "answerPreCheckoutQuery", cancellationToken))
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Message) && update.ChatId.HasValue)
        {
            await SendBotReplyAsync(
                update.ChatId.Value,
                update.UpdateId,
                result.Message,
                result.ReplyMarkup,
                cancellationToken);
        }

        if (result.Invoice is not null)
        {
            var invoiceResponse = await telegramBotGateway.SendInvoiceAsync(result.Invoice, cancellationToken);
            await EnsureSuccessAsync(invoiceResponse, "sendInvoice", cancellationToken);
        }
    }

    private async Task SendBotReplyAsync(
        long chatId,
        long updateId,
        string message,
        BotReplyMarkupDto? replyMarkup,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Sending bot reply. UpdateId={UpdateId}, ChatId={ChatId}, MessagePreview={MessagePreview}",
            updateId,
            chatId,
            message.Length > 160 ? message[..160] : message);

        var outbound = new TelegramBotOutboundMessageDto(chatId, message, replyMarkup);
        var response = await telegramBotGateway.SendMessageAsync(outbound, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation(
                "Bot reply sent successfully. UpdateId={UpdateId}, ChatId={ChatId}",
                updateId,
                chatId);
            return;
        }

        logger.LogWarning(
            "Telegram Bot API call {Endpoint} failed. StatusCode={StatusCode}, ResponseBody={ResponseBody}",
            "sendMessage",
            (int)response.StatusCode,
            response.ResponseBody ?? "(null)");

        if (!TryGetRetryDelay(response, out var retryDelay))
        {
            throw new HttpRequestException($"Telegram Bot API call sendMessage failed with {(int)response.StatusCode}: {response.ResponseBody}");
        }

        logger.LogWarning(
            "Telegram Bot API call {Endpoint} hit rate limit. Queueing retry after {RetryAfterSeconds}s for chat {ChatId}.",
            "sendMessage",
            retryDelay.TotalSeconds,
            chatId);

        PendingReplies[chatId] = new PendingBotReply(outbound, updateId);
        if (ActiveReplyWorkers.TryAdd(chatId, 0))
        {
            _ = Task.Run(() => RetryPendingReplyAsync(chatId, retryDelay), CancellationToken.None);
        }
    }

    private async Task RetryPendingReplyAsync(long chatId, TimeSpan delay)
    {
        try
        {
            var nextDelay = delay;
            while (true)
            {
                await Task.Delay(nextDelay);

                if (!PendingReplies.TryGetValue(chatId, out var pending))
                {
                    return;
                }

                var response = await telegramBotGateway.SendMessageAsync(pending.Message, CancellationToken.None);
                if (response.IsSuccessStatusCode)
                {
                    PendingReplies.TryRemove(chatId, out _);
                    logger.LogInformation(
                        "Queued bot reply sent successfully. UpdateId={UpdateId}, ChatId={ChatId}",
                        pending.UpdateId,
                        chatId);
                    return;
                }

                logger.LogWarning(
                    "Queued Telegram Bot API call sendMessage failed. StatusCode={StatusCode}, ResponseBody={ResponseBody}, ChatId={ChatId}",
                    (int)response.StatusCode,
                    response.ResponseBody ?? "(null)",
                    chatId);

                if (!TryGetRetryDelay(response, out nextDelay))
                {
                    PendingReplies.TryRemove(chatId, out _);
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Queued bot reply retry failed for chat {ChatId}", chatId);
        }
        finally
        {
            ActiveReplyWorkers.TryRemove(chatId, out _);
            if (PendingReplies.ContainsKey(chatId) && ActiveReplyWorkers.TryAdd(chatId, 0))
            {
                _ = Task.Run(() => RetryPendingReplyAsync(chatId, TimeSpan.FromSeconds(2)), CancellationToken.None);
            }
        }
    }

    private async Task ProcessUpdateWithTimeoutAsync(TelegramBotUpdateDto update, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(UpdateProcessingTimeout);

        try
        {
            await ProcessUpdateAsync(update, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogError(
                "Bot update processing timed out. UpdateId={UpdateId}, ChatId={ChatId}, Text={Text}, CallbackData={CallbackData}, SharedChatId={SharedChatId}",
                update.UpdateId,
                update.ChatId,
                update.Text ?? "(null)",
                update.CallbackData ?? "(null)",
                update.SharedChat?.ChatId);

            await errorAlertService.SendAsync(
                "Bot update processing timed out",
                $"UpdateId: {update.UpdateId}\nChatId: {update.ChatId}\nText: {update.Text ?? "(null)"}\nCallbackData: {update.CallbackData ?? "(null)"}\nSharedChatId: {(update.SharedChat is null ? "(null)" : update.SharedChat.ChatId.ToString())}",
                cancellationToken: stoppingToken);
        }
    }

    private async Task<bool> EnsureSuccessAsync(TelegramBotApiResultDto result, string endpoint, CancellationToken cancellationToken)
    {
        if (result.IsSuccessStatusCode)
        {
            return true;
        }

        logger.LogWarning(
            "Telegram Bot API call {Endpoint} failed. StatusCode={StatusCode}, ResponseBody={ResponseBody}",
            endpoint,
            (int)result.StatusCode,
            result.ResponseBody ?? "(null)");

        if (TryGetRetryDelay(result, out var retryDelay))
        {
            logger.LogWarning(
                "Telegram Bot API call {Endpoint} hit rate limit. Respecting retry_after={RetryAfterSeconds}s.",
                endpoint,
                retryDelay.TotalSeconds);
            return false;
        }

        throw new HttpRequestException($"Telegram Bot API call {endpoint} failed with {(int)result.StatusCode}: {result.ResponseBody}");
    }

    private static bool TryGetRetryDelay(TelegramBotApiResultDto result, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.Zero;
        if (result.StatusCode != HttpStatusCode.TooManyRequests || string.IsNullOrWhiteSpace(result.ResponseBody))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(result.ResponseBody);
            if (document.RootElement.TryGetProperty("parameters", out var parameters) &&
                parameters.TryGetProperty("retry_after", out var retryAfterElement) &&
                retryAfterElement.TryGetInt32(out var retryAfterSeconds) &&
                retryAfterSeconds > 0)
            {
                retryDelay = TimeSpan.FromSeconds(retryAfterSeconds);
                return true;
            }
        }
        catch
        {
            // Fall back to a short cooldown below.
        }

        retryDelay = TimeSpan.FromSeconds(3);
        return true;
    }

    private static bool IsTransientPollingStatusCode(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.RequestTimeout;

    private static TimeSpan ResolveTransientPollingDelay(HttpRequestException exception)
    {
        if (exception.StatusCode == HttpStatusCode.TooManyRequests &&
            !string.IsNullOrWhiteSpace(exception.Message))
        {
            var match = RetryAfterRegex.Match(exception.Message);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var retryAfterSeconds) &&
                retryAfterSeconds > 0)
            {
                return TimeSpan.FromSeconds(retryAfterSeconds);
            }
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.BadGateway => TimeSpan.FromSeconds(3),
            HttpStatusCode.GatewayTimeout => TimeSpan.FromSeconds(5),
            HttpStatusCode.ServiceUnavailable => TimeSpan.FromSeconds(5),
            HttpStatusCode.RequestTimeout => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(Math.Max(1, 1 + Math.Min(10, retryAfterBackoffSeed(exception))))
        };

        static int retryAfterBackoffSeed(HttpRequestException ex) =>
            ex.StatusCode == HttpStatusCode.TooManyRequests ? 5 : 2;
    }

    private bool ShouldAlertOnTransientPollingFailure(DateTimeOffset now)
    {
        if (_firstPollingFailureAtUtc is null)
        {
            return false;
        }

        if (now - _firstPollingFailureAtUtc < TransientPollingAlertThreshold)
        {
            return false;
        }

        if (_lastTransientPollingAlertAtUtc.HasValue &&
            now - _lastTransientPollingAlertAtUtc.Value < TransientPollingAlertCooldown)
        {
            return false;
        }

        return true;
    }

    private void ResetTransientPollingFailuresIfNeeded()
    {
        if (_consecutivePollingFailures <= 0)
        {
            return;
        }

        logger.LogInformation(
            "Telegram bot polling recovered after {ConsecutiveFailures} transient failures.",
            _consecutivePollingFailures);

        _consecutivePollingFailures = 0;
        _firstPollingFailureAtUtc = null;
        _lastTransientPollingAlertAtUtc = null;
    }

    private sealed record PendingBotReply(TelegramBotOutboundMessageDto Message, long UpdateId);
}
