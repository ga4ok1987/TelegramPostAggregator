using System.Text.RegularExpressions;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Application.Services;

public sealed class ChannelKeyNormalizer : IChannelKeyNormalizer
{
    private static readonly Regex PrefixRegex = new(@"^(https?://)?t\.me/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Normalize(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed[1..];
        }

        trimmed = PrefixRegex.Replace(trimmed, string.Empty);
        trimmed = trimmed.Trim('/');
        return trimmed.ToLowerInvariant();
    }
}
