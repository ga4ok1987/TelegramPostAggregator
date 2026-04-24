using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotMenuFactory
{
    public const string StartLabel = "\u0421\u0442\u0430\u0440\u0442";
    public const string StopLabel = "\u0421\u0442\u043e\u043f";
    public const string ListLabel = "\u0421\u043f\u0438\u0441\u043e\u043a \u043f\u0456\u0434\u043f\u0438\u0441\u043e\u043a";
    public const string DeleteAllLabel = "\u0412\u0438\u0434\u0430\u043b\u0438\u0442\u0438 \u0432\u0441\u0456";

    private const string ConfirmStopLabel = "\u041f\u0456\u0434\u0442\u0432\u0435\u0440\u0434\u0438\u0442\u0438 \u0441\u0442\u043e\u043f";
    private const string ConfirmDeleteAllLabel = "\u0422\u0430\u043a, \u0432\u0438\u0434\u0430\u043b\u0438\u0442\u0438 \u0432\u0441\u0456";
    private const string ConfirmDeleteOneLabel = "\u0422\u0430\u043a, \u0432\u0438\u0434\u0430\u043b\u0438\u0442\u0438";
    private const string CancelLabel = "\u0421\u043a\u0430\u0441\u0443\u0432\u0430\u0442\u0438";
    private const string RefreshListLabel = "\u041e\u043d\u043e\u0432\u0438\u0442\u0438 \u0441\u043f\u0438\u0441\u043e\u043a";
    private const string PauseAllLabel = "\u041f\u043e\u0441\u0442\u0430\u0432\u0438\u0442\u0438 \u0432\u0441\u0435 \u043d\u0430 \u043f\u0430\u0443\u0437\u0443";
    private const string DeletePrefix = "\u0412\u0438\u0434\u0430\u043b\u0438\u0442\u0438";

    public BotReplyMarkupDto BuildMainMenu() =>
        new(
            [
                [new BotButtonDto(StartLabel), new BotButtonDto(StopLabel)],
                [new BotButtonDto(ListLabel)],
                [new BotButtonDto(DeleteAllLabel)]
            ]);

    public BotReplyMarkupDto BuildPauseConfirmationMenu() =>
        new(
            [
                [new BotButtonDto(ConfirmStopLabel, "pause_all:confirm")],
                [new BotButtonDto(CancelLabel, "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildDeleteAllConfirmationMenu() =>
        new(
            [
                [new BotButtonDto(ConfirmDeleteAllLabel, "delete_all:confirm")],
                [new BotButtonDto(CancelLabel, "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildDeleteOneConfirmationMenu(Guid channelId) =>
        new(
            [
                [new BotButtonDto(ConfirmDeleteOneLabel, $"delete_one:confirm:{channelId}")],
                [new BotButtonDto(CancelLabel, "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildSubscriptionsMenu(IReadOnlyList<SubscriptionDto> subscriptions)
    {
        var buttons = subscriptions
            .Select(subscription => (IReadOnlyList<BotButtonDto>)[new BotButtonDto($"{DeletePrefix} {subscription.ChannelName}", $"delete_one:{subscription.ChannelId}")])
            .ToList();

        buttons.Add([new BotButtonDto(RefreshListLabel, "menu:list")]);
        buttons.Add([new BotButtonDto(PauseAllLabel, "menu:stop")]);
        buttons.Add([new BotButtonDto(DeleteAllLabel, "menu:delete_all")]);

        return new BotReplyMarkupDto(buttons, IsInline: true);
    }
}
