using System.Net;

namespace TelegramPostAggregator.Application.DTOs;

public sealed record TelegramBotOutboundMessageDto(
    long ChatId,
    string Text,
    BotReplyMarkupDto? ReplyMarkup = null,
    bool DisableWebPagePreview = true,
    string? ParseMode = null);

public sealed record TelegramBotMediaMessageDto(
    long ChatId,
    string FilePath,
    string Caption,
    string MediaKind,
    string? ParseMode = null);

public sealed record TelegramBotMediaGroupItemDto(
    string FilePath,
    string MediaKind,
    string? Caption = null,
    string? ParseMode = null);

public sealed record TelegramBotMediaGroupMessageDto(
    long ChatId,
    IReadOnlyList<TelegramBotMediaGroupItemDto> Items);

public sealed record TelegramBotApiResultDto(
    bool IsSuccessStatusCode,
    HttpStatusCode StatusCode,
    string? ResponseBody = null);
