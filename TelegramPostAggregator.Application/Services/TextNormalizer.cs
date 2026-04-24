using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Application.Services;

public sealed class TextNormalizer : ITextNormalizer
{
    private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Normalize(string? rawText)
    {
        var value = rawText ?? string.Empty;
        value = UrlRegex.Replace(value, " ");
        value = WhiteSpaceRegex.Replace(value, " ");
        return value.Trim().ToLowerInvariant();
    }

    public string ComputeHash(string normalizedText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return Convert.ToHexString(bytes);
    }
}
