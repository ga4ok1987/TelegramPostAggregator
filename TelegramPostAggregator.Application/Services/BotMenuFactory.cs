using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public static class BotMenuFactory
{
    public const string StartMonitoringButton = "\u0421\u0442\u0430\u0440\u0442";
    public const string StopMonitoringButton = "\u0421\u0442\u043e\u043f";
    public const string SubscriptionButton = "\u041f\u0456\u0434\u043f\u0438\u0441\u043a\u0430";
    public const string SubscriptionListButton = "\u0421\u043f\u0438\u0441\u043e\u043a \u043f\u0456\u0434\u043f\u0438\u0441\u043e\u043a";
    public const string SettingsButton = "\u041d\u0430\u043b\u0430\u0448\u0442\u0443\u0432\u0430\u043d\u043d\u044f";
    public const string UkrainianLanguageButton = "\ud83c\uddfa\ud83c\udde6 \u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430";
    public const string EnglishLanguageButton = "\ud83c\uddec\ud83c\udde7 English";
    public const string DeleteAllChannelsButton = "\u0412\u0438\u0434\u0430\u043b\u0438\u0442\u0438 \u0432\u0441\u0456 \u043a\u0430\u043d\u0430\u043b\u0438";
    public const string BackButton = "\u041d\u0430\u0437\u0430\u0434";
    private const string DeletePrefix = "\u0412\u0438\u0434\u0430\u043b\u0438\u0442\u0438 ";

    public static TelegramBotReplyMarkupDto MainMenu() =>
        new([
            [StartMonitoringButton, StopMonitoringButton],
            [SubscriptionButton, SubscriptionListButton],
            [SettingsButton],
            [UkrainianLanguageButton, EnglishLanguageButton]
        ]);

    public static TelegramBotReplyMarkupDto SubscriptionManagementMenu(IReadOnlyList<ChannelDto> channels) =>
        new(
            [[DeleteAllChannelsButton], .. channels.Select(channel => new[] { BuildDeleteButton(channel.ChannelReference) }), [BackButton]],
            ResizeKeyboard: true,
            OneTimeKeyboard: false);

    public static string BuildDeleteButton(string channelReference) =>
        $"{DeletePrefix}{NormalizeReferenceLabel(channelReference)}";

    public static bool TryParseDeleteButton(string input, out string channelReference)
    {
        channelReference = string.Empty;
        if (!input.StartsWith(DeletePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        channelReference = input[DeletePrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(channelReference);
    }

    private static string NormalizeReferenceLabel(string channelReference)
    {
        var trimmed = channelReference.Trim();
        if (trimmed.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = trimmed["https://t.me/".Length..].Trim('/');
            return slug.StartsWith('+') ? $"t.me/{slug}" : $"@{slug}";
        }

        if (trimmed.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = trimmed["http://t.me/".Length..].Trim('/');
            return slug.StartsWith('+') ? $"t.me/{slug}" : $"@{slug}";
        }

        return trimmed.StartsWith('@') ? trimmed : $"@{trimmed}";
    }
}
