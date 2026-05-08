using System.Text.RegularExpressions;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Application.Services;

public sealed class ChannelKeyNormalizer : IChannelKeyNormalizer
{
    private static readonly Regex PrefixRegex = new(@"^(https?://)?t\.me/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly char[] TrailingNoiseCharacters = ['\'', '"', ',', '.', ';', ':', '!', '?', ')', ']', '}'];

    public string Normalize(string input)
    {
        var trimmed = input.Trim().TrimEnd(TrailingNoiseCharacters);
        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed[1..];
        }

        trimmed = PrefixRegex.Replace(trimmed, string.Empty);
        trimmed = trimmed.Trim('/');
        if (trimmed.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["s/".Length..].Trim('/');
        }

        if (!trimmed.StartsWith("joinchat/", StringComparison.OrdinalIgnoreCase))
        {
            var slashIndex = trimmed.IndexOf('/');
            if (slashIndex >= 0)
            {
                trimmed = trimmed[..slashIndex];
            }
        }

        return trimmed.ToLowerInvariant();
    }
}
