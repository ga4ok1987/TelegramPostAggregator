namespace TelegramPostAggregator.Application.DTOs;

public sealed record BotUserSnapshotDto(long TelegramUserId, string TelegramUsername, string DisplayName, string? LanguageCode);

public sealed record TelegramBotUpdateDto(
    long UpdateId,
    BotUserSnapshotDto User,
    string? Text,
    string? CallbackQueryId,
    string? CallbackData,
    long? ChatId,
    DateTimeOffset ReceivedAtUtc,
    TelegramSharedChatDto? SharedChat = null);

public sealed record BotCommandResultDto(
    bool Success,
    string Message,
    BotReplyMarkupDto? ReplyMarkup = null,
    string? CallbackNotification = null);

public sealed record BotReplyMarkupDto(
    IReadOnlyList<IReadOnlyList<BotButtonDto>> Buttons,
    bool IsInline = false,
    bool ResizeKeyboard = true);

public sealed record BotButtonDto(
    string Text,
    string? CallbackData = null,
    string? WebAppUrl = null,
    BotButtonRequestChatDto? RequestChat = null);

public sealed record TelegramSharedChatDto(
    int RequestId,
    long ChatId,
    string? Title,
    string? Username);

public sealed record BotButtonRequestChatDto(
    int RequestId,
    bool ChatIsChannel,
    bool RequestTitle,
    bool RequestUsername,
    bool BotIsMember,
    BotChatAdministratorRightsDto? UserAdministratorRights = null,
    BotChatAdministratorRightsDto? BotAdministratorRights = null);

public sealed record BotChatAdministratorRightsDto(
    bool CanManageChat = false,
    bool CanDeleteMessages = false,
    bool CanManageVideoChats = false,
    bool CanRestrictMembers = false,
    bool CanPromoteMembers = false,
    bool CanChangeInfo = false,
    bool CanInviteUsers = false,
    bool CanPostMessages = false,
    bool CanEditMessages = false,
    bool CanPinMessages = false,
    bool CanPostStories = false,
    bool CanEditStories = false,
    bool CanDeleteStories = false,
    bool CanManageDirectMessages = false);
