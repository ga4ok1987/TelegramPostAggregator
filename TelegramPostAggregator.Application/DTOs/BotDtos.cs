namespace TelegramPostAggregator.Application.DTOs;

public sealed record BotUserSnapshotDto(long TelegramUserId, string TelegramUsername, string DisplayName, string? LanguageCode);

public sealed record TelegramBotUpdateDto(
    long UpdateId,
    BotUserSnapshotDto User,
    string? Text,
    long? ChatId,
    DateTimeOffset ReceivedAtUtc);

public sealed record TelegramBotReplyMarkupDto(
    IReadOnlyList<IReadOnlyList<string>> Keyboard,
    bool ResizeKeyboard = true,
    bool OneTimeKeyboard = false);

public sealed record BotCommandResultDto(bool Success, string Message, TelegramBotReplyMarkupDto? ReplyMarkup = null);

public sealed record TelegramBotOutboundMessageDto(
    long ChatId,
    string Text,
    bool DisableWebPagePreview = false,
    TelegramBotReplyMarkupDto? ReplyMarkup = null,
    string? ParseMode = null);

public sealed record TelegramBotMediaMessageDto(long ChatId, string FilePath, string Caption, string? ParseMode = null);

public sealed record TelegramBotMediaGroupItemDto(string Type, string FilePath, string Caption, string? ParseMode = null);

public sealed record TelegramBotMediaGroupMessageDto(long ChatId, IReadOnlyList<TelegramBotMediaGroupItemDto> Items);
