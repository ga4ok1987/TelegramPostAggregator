using TdLib;

namespace TelegramPostAggregator.Infrastructure.Services;

internal static class TelegramContentClassifier
{
    private static readonly HashSet<string> ServiceContentTypes =
    [
        "messageBasicGroupChatCreate",
        "messageChatAddMembers",
        "messageChatChangePhoto",
        "messageChatChangeTitle",
        "messageChatDeleteMember",
        "messageChatDeletePhoto",
        "messageChatJoinByLink",
        "messageChatJoinByRequest",
        "messageChatSetBackground",
        "messageChatSetMessageAutoDeleteTime",
        "messageChatSetTheme",
        "messageChatUpgradeFrom",
        "messageChatUpgradeTo",
        "messageContactRegistered",
        "messageCustomServiceAction",
        "messageExpiredPhoto",
        "messageExpiredVideo",
        "messageForumTopicCreated",
        "messageForumTopicEdited",
        "messageForumTopicIsClosedToggled",
        "messageForumTopicIsHiddenToggled",
        "messageGameScore",
        "messageInviteVideoChatParticipants",
        "messagePaymentSuccessful",
        "messagePaymentSuccessfulBot",
        "messagePinMessage",
        "messageProximityAlertTriggered",
        "messageScreenshotTaken",
        "messageSuggestProfilePhoto",
        "messageSupergroupChatCreate",
        "messageVideoChatEnded",
        "messageVideoChatScheduled",
        "messageVideoChatStarted",
        "messageWebAppDataSent"
    ];

    public static bool IsIgnorableContent(TdApi.MessageContent content) =>
        IsIgnorableContentType(content.DataType);

    public static bool IsIgnorableContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        (contentType.StartsWith("messageGiveaway", StringComparison.Ordinal) ||
         ServiceContentTypes.Contains(contentType));
}
