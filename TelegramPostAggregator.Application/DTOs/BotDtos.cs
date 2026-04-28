namespace TelegramPostAggregator.Application.DTOs;

public sealed record BotUserSnapshotDto(long TelegramUserId, string TelegramUsername, string DisplayName, string? LanguageCode);

public sealed record TelegramBotUpdateDto(
    long UpdateId,
    BotUserSnapshotDto User,
    string? Text,
    string? CallbackQueryId,
    string? CallbackData,
    long? ChatId,
    DateTimeOffset ReceivedAtUtc);

public sealed record BotCommandResultDto(
    bool Success,
    string Message,
    BotReplyMarkupDto? ReplyMarkup = null,
    string? CallbackNotification = null);

public sealed record BotReplyMarkupDto(
    IReadOnlyList<IReadOnlyList<BotButtonDto>> Buttons,
    bool IsInline = false,
    bool ResizeKeyboard = true);

public sealed record BotButtonDto(string Text, string? CallbackData = null, string? WebAppUrl = null);
