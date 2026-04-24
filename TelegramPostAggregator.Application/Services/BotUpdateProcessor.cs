using Microsoft.Extensions.Logging;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class BotUpdateProcessor(
    IUserService userService,
    IChannelTrackingService channelTrackingService,
    IChannelReferenceValidator channelReferenceValidator,
    ILogger<BotUpdateProcessor> logger) : IBotUpdateProcessor
{
    private const string MonitoringEnabledMessage = "\u041c\u043e\u043d\u0456\u0442\u043e\u0440\u0438\u043d\u0433 \u0443\u0432\u0456\u043c\u043a\u043d\u0435\u043d\u043e. \u041d\u0430\u0434\u0456\u0448\u043b\u0456\u0442\u044c \u043f\u043e\u0441\u0438\u043b\u0430\u043d\u043d\u044f \u043d\u0430 \u043a\u0430\u043d\u0430\u043b \u0430\u0431\u043e \u043d\u0430\u0442\u0438\u0441\u043d\u0456\u0442\u044c \u041f\u0456\u0434\u043f\u0438\u0441\u043a\u0430.";
    private const string MonitoringStoppedMessage = "\u041c\u043e\u043d\u0456\u0442\u043e\u0440\u0438\u043d\u0433 \u0437\u0443\u043f\u0438\u043d\u0435\u043d\u043e. \u041d\u043e\u0432\u0456 \u043f\u043e\u0441\u0442\u0438 \u0431\u0456\u043b\u044c\u0448\u0435 \u043d\u0435 \u043d\u0430\u0434\u0445\u043e\u0434\u0438\u0442\u0438\u043c\u0443\u0442\u044c, \u0434\u043e\u043a\u0438 \u0432\u0438 \u043d\u0435 \u043d\u0430\u0442\u0438\u0441\u043d\u0435\u0442\u0435 \u0421\u0442\u0430\u0440\u0442.";
    private const string SubscriptionPromptMessage = "\u041d\u0430\u0434\u0456\u0448\u043b\u0456\u0442\u044c @channel \u0430\u0431\u043e \u043f\u043e\u0441\u0438\u043b\u0430\u043d\u043d\u044f \u0432\u0438\u0434\u0443 https://t.me/channel_name";
    private const string SettingsMessage = "\u041d\u0430\u043b\u0430\u0448\u0442\u0443\u0432\u0430\u043d\u043d\u044f \u043f\u0456\u0434\u0433\u043e\u0442\u0443\u0454\u043c\u043e \u043e\u043a\u0440\u0435\u043c\u043e. \u0417\u0430\u0440\u0430\u0437 \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u0456 \u0421\u0442\u0430\u0440\u0442, \u0421\u0442\u043e\u043f, \u041f\u0456\u0434\u043f\u0438\u0441\u043a\u0430 \u0456 \u0421\u043f\u0438\u0441\u043e\u043a \u043f\u0456\u0434\u043f\u0438\u0441\u043e\u043a.";
    private const string MainMenuMessage = "\u0413\u043e\u043b\u043e\u0432\u043d\u0435 \u043c\u0435\u043d\u044e.";
    private const string NoSubscriptionsMessage = "\u0423 \u0432\u0430\u0441 \u0449\u0435 \u043d\u0435\u043c\u0430\u0454 \u043f\u0456\u0434\u043f\u0438\u0441\u043e\u043a.";
    private const string SubscriptionsHeader = "\u0412\u0430\u0448\u0456 \u043f\u0456\u0434\u043f\u0438\u0441\u043a\u0438:";
    private const string RemovedSubscriptionMessage = "\u0412\u0438\u0434\u0430\u043b\u0438\u0432 \u043f\u0456\u0434\u043f\u0438\u0441\u043a\u0443: ";
    private const string RemovedAllSubscriptionsMessage = "\u0423\u0441\u0456 \u043f\u0456\u0434\u043f\u0438\u0441\u043a\u0438 \u0432\u0438\u0434\u0430\u043b\u0435\u043d\u043e.";
    private const string InvalidChannelPrompt = "\u041d\u0430\u0434\u0456\u0448\u043b\u0456\u0442\u044c \u043f\u0443\u0431\u043b\u0456\u0447\u043d\u0438\u0439 \u043a\u0430\u043d\u0430\u043b \u0443 \u0444\u043e\u0440\u043c\u0430\u0442\u0456 @channel_name \u0430\u0431\u043e https://t.me/channel_name";
    private const string AddedSubscriptionMessage = "\u041f\u0456\u0434\u043f\u0438\u0441\u043a\u0443 \u0434\u043e\u0434\u0430\u043d\u043e: ";
    private const string UkrainianLanguageSelectedMessage = "\u041c\u043e\u0432\u0443 \u0437\u043c\u0456\u043d\u0435\u043d\u043e \u043d\u0430 \u0443\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0443.";
    private const string EnglishLanguageSelectedMessage = "Language changed to English.";

    public async Task<BotCommandResultDto> ProcessAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken = default)
    {
        await userService.UpsertTelegramUserAsync(update.User, cancellationToken);

        var text = update.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BotCommandResultDto(true, "Empty update ignored.", BotMenuFactory.MainMenu());
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) || string.Equals(text, BotMenuFactory.StartMonitoringButton, StringComparison.Ordinal))
        {
            await userService.SetMonitoringEnabledAsync(update.User.TelegramUserId, true, cancellationToken);
            return new BotCommandResultDto(true, MonitoringEnabledMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.StopMonitoringButton, StringComparison.Ordinal))
        {
            await userService.SetMonitoringEnabledAsync(update.User.TelegramUserId, false, cancellationToken);
            return new BotCommandResultDto(true, MonitoringStoppedMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.SubscriptionButton, StringComparison.Ordinal))
        {
            return new BotCommandResultDto(true, SubscriptionPromptMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.SettingsButton, StringComparison.Ordinal))
        {
            return new BotCommandResultDto(true, SettingsMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.UkrainianLanguageButton, StringComparison.Ordinal))
        {
            await userService.SetPreferredLanguageAsync(update.User.TelegramUserId, "uk", cancellationToken);
            return new BotCommandResultDto(true, UkrainianLanguageSelectedMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.EnglishLanguageButton, StringComparison.Ordinal))
        {
            await userService.SetPreferredLanguageAsync(update.User.TelegramUserId, "en", cancellationToken);
            return new BotCommandResultDto(true, EnglishLanguageSelectedMessage, BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.BackButton, StringComparison.Ordinal))
        {
            return new BotCommandResultDto(true, MainMenuMessage, BotMenuFactory.MainMenu());
        }

        if (text.StartsWith("/list", StringComparison.OrdinalIgnoreCase) || string.Equals(text, BotMenuFactory.SubscriptionListButton, StringComparison.Ordinal))
        {
            var channels = await channelTrackingService.ListTrackedChannelsAsync(update.User.TelegramUserId, cancellationToken);
            if (channels.Count == 0)
            {
                return new BotCommandResultDto(true, NoSubscriptionsMessage, BotMenuFactory.MainMenu());
            }

            var message = string.Join(Environment.NewLine, channels.Select((channel, index) => $"{index + 1}. {channel.ChannelName} [{channel.Status}]"));
            return new BotCommandResultDto(true, $"{SubscriptionsHeader}{Environment.NewLine}{message}", BotMenuFactory.SubscriptionManagementMenu(channels));
        }

        if (text.StartsWith("/remove", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return new BotCommandResultDto(false, "Usage: /remove <channel>", BotMenuFactory.MainMenu());
            }

            await channelTrackingService.RemoveTrackedChannelAsync(
                new RemoveTrackedChannelDto(update.User.TelegramUserId, parts[1]),
                cancellationToken);

            return new BotCommandResultDto(true, $"{RemovedSubscriptionMessage}{parts[1]}", BotMenuFactory.MainMenu());
        }

        if (string.Equals(text, BotMenuFactory.DeleteAllChannelsButton, StringComparison.Ordinal))
        {
            await channelTrackingService.RemoveAllTrackedChannelsAsync(update.User.TelegramUserId, cancellationToken);
            return new BotCommandResultDto(true, RemovedAllSubscriptionsMessage, BotMenuFactory.MainMenu());
        }

        if (BotMenuFactory.TryParseDeleteButton(text, out var channelReference))
        {
            await channelTrackingService.RemoveTrackedChannelAsync(
                new RemoveTrackedChannelDto(update.User.TelegramUserId, channelReference),
                cancellationToken);

            var channels = await channelTrackingService.ListTrackedChannelsAsync(update.User.TelegramUserId, cancellationToken);
            if (channels.Count == 0)
            {
                return new BotCommandResultDto(true, $"{RemovedSubscriptionMessage}{channelReference}", BotMenuFactory.MainMenu());
            }

            var message = string.Join(Environment.NewLine, channels.Select((channel, index) => $"{index + 1}. {channel.ChannelName} [{channel.Status}]"));
            return new BotCommandResultDto(
                true,
                $"{RemovedSubscriptionMessage}{channelReference}{Environment.NewLine}{Environment.NewLine}{SubscriptionsHeader}{Environment.NewLine}{message}",
                BotMenuFactory.SubscriptionManagementMenu(channels));
        }

        if (!channelReferenceValidator.IsValid(text))
        {
            logger.LogInformation(
                "Ignoring unsupported bot input from user {TelegramUserId}: {Input}",
                update.User.TelegramUserId,
                text);
            return new BotCommandResultDto(false, InvalidChannelPrompt, BotMenuFactory.MainMenu());
        }

        await channelTrackingService.AddTrackedChannelAsync(
            new AddTrackedChannelDto(update.User.TelegramUserId, update.User.TelegramUsername, update.User.DisplayName, text),
            cancellationToken);

        return new BotCommandResultDto(true, $"{AddedSubscriptionMessage}{text}", BotMenuFactory.MainMenu());
    }
}
