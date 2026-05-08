namespace TelegramPostAggregator.Application.DTOs;

public sealed record BotUserSnapshotDto(long TelegramUserId, string TelegramUsername, string DisplayName, string? LanguageCode);

public sealed record TelegramBotUpdateDto(
    long UpdateId,
    BotUserSnapshotDto? User,
    string? Text,
    string? CallbackQueryId,
    string? CallbackData,
    long? ChatId,
    DateTimeOffset ReceivedAtUtc,
    TelegramSharedChatDto? SharedChat = null,
    TelegramMyChatMemberDto? MyChatMember = null,
    bool IsChannelPost = false,
    TelegramPreCheckoutQueryDto? PreCheckoutQuery = null,
    TelegramSuccessfulPaymentDto? SuccessfulPayment = null);

public sealed record BotCommandResultDto(
    bool Success,
    string Message,
    BotReplyMarkupDto? ReplyMarkup = null,
    string? CallbackNotification = null,
    TelegramBotInvoiceDto? Invoice = null,
    TelegramPreCheckoutResponseDto? PreCheckoutResponse = null);

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

public sealed record TelegramMyChatMemberDto(
    long ChatId,
    string? ChatType,
    string? Title,
    string? Username,
    string? OldStatus,
    string? NewStatus);

public sealed record TelegramPreCheckoutQueryDto(
    string Id,
    string Currency,
    int TotalAmount,
    string InvoicePayload);

public sealed record TelegramSuccessfulPaymentDto(
    string Currency,
    int TotalAmount,
    string InvoicePayload,
    string TelegramPaymentChargeId,
    DateTimeOffset? SubscriptionExpirationDate = null,
    bool IsRecurring = false,
    bool IsFirstRecurring = false);

public sealed record TelegramPreCheckoutResponseDto(
    string PreCheckoutQueryId,
    bool Ok,
    string? ErrorMessage = null);

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
