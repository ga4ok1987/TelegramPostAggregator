using System.Text.RegularExpressions;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Application.Services;

public sealed class ChannelReferenceValidator : IChannelReferenceValidator
{
    private static readonly Regex UsernameRegex = new(
        @"^[A-Za-z][A-Za-z0-9_]{3,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TelegramLinkRegex = new(
        @"^(?:https?://)?t\.me/(?:(?:joinchat/|\+)[A-Za-z0-9_-]+|[A-Za-z][A-Za-z0-9_]{3,31})(?:[/?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public bool IsValid(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith('/'))
        {
            return false;
        }

        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed[1..];
        }

        return UsernameRegex.IsMatch(trimmed) || TelegramLinkRegex.IsMatch(input.Trim());
    }
}
