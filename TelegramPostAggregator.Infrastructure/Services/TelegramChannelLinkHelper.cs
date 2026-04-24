using System.Text.RegularExpressions;

namespace TelegramPostAggregator.Infrastructure.Services;

internal static partial class TelegramChannelLinkHelper
{
    public static bool IsInviteLink(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var value = reference.Trim();
        return value.Contains("/+", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("joinchat", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith('+');
    }

    public static string? BuildChannelUrl(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var value = reference.Trim();
        if (value.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://t.me/{value["http://t.me/".Length..]}";
        }

        if (value.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{value}";
        }

        if (value.StartsWith('@'))
        {
            return $"https://t.me/{value[1..]}";
        }

        return $"https://t.me/{value.TrimStart('/')}";
    }

    public static string NormalizeInviteLink(string reference) =>
        reference.Trim().StartsWith('+')
            ? $"https://t.me/{reference.Trim()}"
            : reference.Trim();

    public static string ResolvePublicUsername(string reference, string fallback)
    {
        var value = reference.Trim();
        if (value.StartsWith('@'))
        {
            return value[1..];
        }

        var match = TelegramLinkRegex().Match(value);
        return match.Success ? match.Groups["slug"].Value : fallback;
    }

    public static string? BuildPublicPostUrl(string reference, long messageId)
    {
        var value = reference.Trim();
        var match = TelegramLinkRegex().Match(value);
        if (match.Success)
        {
            return $"https://t.me/{match.Groups["slug"].Value}/{messageId}";
        }

        return value.StartsWith('@')
            ? $"https://t.me/{value[1..]}/{messageId}"
            : null;
    }

    public static bool TryBuildPrivatePostUrl(long chatId, long messageId, out string url)
    {
        url = string.Empty;
        if (chatId >= 0)
        {
            return false;
        }

        var internalChatId = Math.Abs(chatId).ToString();
        if (internalChatId.StartsWith("100", StringComparison.Ordinal) && internalChatId.Length > 3)
        {
            internalChatId = internalChatId[3..];
        }

        url = $"https://t.me/c/{internalChatId}/{messageId}";
        return true;
    }

    [GeneratedRegex(@"^(?:https?://)?t\.me/(?<slug>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramLinkRegex();
}
