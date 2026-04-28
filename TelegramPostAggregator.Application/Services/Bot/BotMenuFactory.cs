using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotMenuFactory(
    BotLocalizationCatalog localizationCatalog,
    IOptions<MiniAppOptions> miniAppOptions)
{
    private readonly string _miniAppUrl = miniAppOptions.Value.Url.Trim();

    public BotReplyMarkupDto BuildMainMenu(string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        var buttons = new List<IReadOnlyList<BotButtonDto>>
        {
            new List<BotButtonDto> { new(locale.StartLabel), new(locale.StopLabel) },
            new List<BotButtonDto> { new(locale.ListLabel) },
            new List<BotButtonDto> { new(locale.DeleteAllLabel) }
        };

        if (!string.IsNullOrWhiteSpace(_miniAppUrl))
        {
            buttons.Add(new List<BotButtonDto> { new(localizationCatalog.MiniAppButtonLabel, WebAppUrl: _miniAppUrl) });
        }

        buttons.Add(new List<BotButtonDto> { new(localizationCatalog.BuildLanguageButtonLabel(languageCode)) });
        return new BotReplyMarkupDto(buttons);
    }

    public BotReplyMarkupDto BuildPauseConfirmationMenu(string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return new BotReplyMarkupDto(
            [
                [new BotButtonDto(locale.ConfirmStopLabel, "pause_all:confirm")],
                [new BotButtonDto(locale.CancelLabel, "action:cancel")]
            ],
            IsInline: true);
    }

    public BotReplyMarkupDto BuildDeleteAllConfirmationMenu(string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return new BotReplyMarkupDto(
            [
                [new BotButtonDto(locale.ConfirmDeleteAllLabel, "delete_all:confirm")],
                [new BotButtonDto(locale.CancelLabel, "action:cancel")]
            ],
            IsInline: true);
    }

    public BotReplyMarkupDto BuildDeleteOneConfirmationMenu(Guid channelId, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return new BotReplyMarkupDto(
            [
                [new BotButtonDto(locale.ConfirmDeleteOneLabel, $"delete_one:confirm:{channelId}")],
                [new BotButtonDto(locale.CancelLabel, "action:cancel")]
            ],
            IsInline: true);
    }

    public BotReplyMarkupDto BuildSubscriptionsMenu(IReadOnlyList<SubscriptionDto> subscriptions, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        var buttons = subscriptions
            .Select(subscription => (IReadOnlyList<BotButtonDto>)[new BotButtonDto($"{locale.DeletePrefix} {subscription.ChannelName}", $"delete_one:{subscription.ChannelId}")])
            .ToList();

        buttons.Add([new BotButtonDto(locale.RefreshListLabel, "menu:list")]);
        buttons.Add([new BotButtonDto(locale.PauseAllLabel, "menu:stop")]);
        buttons.Add([new BotButtonDto(locale.DeleteAllLabel, "menu:delete_all")]);

        return new BotReplyMarkupDto(buttons, IsInline: true);
    }

    public BotReplyMarkupDto BuildLanguageMenu(string languageCode)
    {
        var currentCode = localizationCatalog.NormalizeLanguageCode(languageCode);
        var buttons = localizationCatalog.GetSupportedLanguages()
            .Select(option =>
            {
                var prefix = string.Equals(option.Code, currentCode, StringComparison.OrdinalIgnoreCase) ? "✓ " : string.Empty;
                return new BotButtonDto($"{prefix}{option.Flag} {option.Name}", $"language:set:{option.Code}");
            })
            .Chunk(2)
            .Select(chunk => (IReadOnlyList<BotButtonDto>)chunk.ToList())
            .ToList();

        buttons.Add([new BotButtonDto(localizationCatalog.GetLocale(languageCode).CancelLabel, "action:cancel")]);
        return new BotReplyMarkupDto(buttons, IsInline: true);
    }
}
