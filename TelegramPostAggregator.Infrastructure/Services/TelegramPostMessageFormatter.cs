namespace TelegramPostAggregator.Infrastructure.Services;

public static class TelegramPostMessageFormatter
{
    private const int MessageTextLimit = 3500;
    private const int CaptionTextLimit = 1000;
    private const string BlockSeparator = "\n\n";
    private static readonly string[] EmptyMediaTextMarkers =
    [
        "(media post)",
        "(photo post)",
        "(video post)",
        "(no text)"
    ];

    public static IReadOnlyList<string> FormatMessageParts(string channelName, string rawText, string? originalPostUrl) =>
        SplitForTelegram(ComposeFullText(channelName, rawText, originalPostUrl), MessageTextLimit);

    public static IReadOnlyList<string> FormatMessagePartsHtml(string channelName, string rawText, string? originalPostUrl, string? channelUrl = null)
    {
        var header = BuildHeaderHtml(channelName, channelUrl);
        var body = string.IsNullOrWhiteSpace(rawText) ? "(no text)" : rawText.Trim();
        var footer = string.IsNullOrWhiteSpace(originalPostUrl) ? string.Empty : HtmlEncode(originalPostUrl.Trim());
        var singleMessage = ComposeHtmlMessage(header, HtmlEncode(body), footer);
        if (singleMessage.Length <= MessageTextLimit)
        {
            return [singleMessage];
        }

        var parts = new List<string>();
        var remaining = body;
        var firstBodyLimit = Math.Max(0, MessageTextLimit - header.Length - BlockSeparator.Length);
        var firstChunk = TakeHtmlEncodedChunk(remaining, firstBodyLimit);
        parts.Add($"{header}{BlockSeparator}{firstChunk.Encoded}");
        remaining = remaining[firstChunk.ConsumedLength..].TrimStart();

        while (!string.IsNullOrWhiteSpace(remaining))
        {
            var remainingEncoded = HtmlEncode(remaining);
            var canFitLast = string.IsNullOrWhiteSpace(footer)
                ? remainingEncoded.Length <= MessageTextLimit
                : remainingEncoded.Length + BlockSeparator.Length + footer.Length <= MessageTextLimit;

            if (canFitLast)
            {
                parts.Add(string.IsNullOrWhiteSpace(footer)
                    ? remainingEncoded
                    : $"{remainingEncoded}{BlockSeparator}{footer}");
                break;
            }

            var chunk = TakeHtmlEncodedChunk(remaining, MessageTextLimit);
            parts.Add(chunk.Encoded);
            remaining = remaining[chunk.ConsumedLength..].TrimStart();
        }

        return parts;
    }

    public static CaptionRenderResult FormatCaption(string channelName, string rawText, string? originalPostUrl)
    {
        var fullText = ComposeFullText(channelName, rawText, originalPostUrl);
        if (fullText.Length <= CaptionTextLimit)
        {
            return new CaptionRenderResult(fullText, []);
        }

        var splitAt = FindSplitIndex(fullText, CaptionTextLimit);
        var caption = fullText[..splitAt].TrimEnd();
        var overflow = fullText[splitAt..].TrimStart();
        return new CaptionRenderResult(caption, SplitForTelegram(overflow, MessageTextLimit));
    }

    public static string FormatCaptionHtml(string channelName, string rawText, string? originalPostUrl, string? channelUrl = null)
    {
        var header = BuildHeaderHtml(channelName, channelUrl);
        var body = NormalizeCaptionBody(rawText);
        var link = string.IsNullOrWhiteSpace(originalPostUrl) ? string.Empty : HtmlEncode(originalPostUrl.Trim());
        var footer = string.IsNullOrWhiteSpace(link) ? string.Empty : $"{BlockSeparator}{link}";
        var bodySeparator = string.IsNullOrWhiteSpace(body) ? string.Empty : BlockSeparator;
        var fixedLength = header.Length + bodySeparator.Length + footer.Length;
        var maxBodyLength = Math.Max(0, CaptionTextLimit - fixedLength);
        var text = HtmlEncode(body);

        if (text.Length > maxBodyLength)
        {
            text = TrimEncodedBodyToLength(body, maxBodyLength);
        }

        return string.IsNullOrWhiteSpace(text)
            ? $"{header}{footer}"
            : $"{header}{BlockSeparator}{text}{footer}";
    }

    private static string ComposeFullText(string channelName, string rawText, string? originalPostUrl)
    {
        var text = string.IsNullOrWhiteSpace(rawText) ? "(no text)" : rawText.Trim();
        var header = channelName.Trim();

        return string.IsNullOrWhiteSpace(originalPostUrl)
            ? string.IsNullOrWhiteSpace(header) ? text : $"{header}{BlockSeparator}{text}"
            : string.IsNullOrWhiteSpace(header) ? $"{text}{BlockSeparator}{originalPostUrl}" : $"{header}{BlockSeparator}{text}{BlockSeparator}{originalPostUrl}";
    }

    private static string ComposeHtmlMessage(string header, string body, string footer)
    {
        var message = string.IsNullOrWhiteSpace(body)
            ? header
            : $"{header}{BlockSeparator}{body}";

        return string.IsNullOrWhiteSpace(footer)
            ? message
            : $"{message}{BlockSeparator}{footer}";
    }

    private static IReadOnlyList<string> SplitForTelegram(string text, int maxLength)
    {
        var normalized = text.Trim();
        if (normalized.Length <= maxLength)
        {
            return [normalized];
        }

        var parts = new List<string>();
        var remaining = normalized;
        while (remaining.Length > maxLength)
        {
            var splitAt = FindSplitIndex(remaining, maxLength);
            parts.Add(remaining[..splitAt].TrimEnd());
            remaining = remaining[splitAt..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            parts.Add(remaining);
        }

        return parts;
    }

    private static int FindSplitIndex(string text, int maxLength)
    {
        var splitAt = Math.Min(maxLength, text.Length);
        if (splitAt == text.Length)
        {
            return splitAt;
        }

        var lastSeparator = text.LastIndexOfAny([' ', '\n', '\r', '\t'], splitAt - 1, splitAt);
        return lastSeparator > 0 ? lastSeparator : splitAt;
    }

    private static string TrimToLength(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var splitAt = FindSplitIndex(text, maxLength);
        return text[..splitAt].TrimEnd();
    }

    private static string NormalizeCaptionBody(string rawText)
    {
        var text = string.IsNullOrWhiteSpace(rawText) ? string.Empty : rawText.Trim();
        return EmptyMediaTextMarkers.Any(marker => string.Equals(text, marker, StringComparison.OrdinalIgnoreCase))
            ? string.Empty
            : text;
    }

    private static string TrimEncodedBodyToLength(string body, int maxLength)
    {
        if (maxLength <= 3)
        {
            return string.Empty;
        }

        var candidate = body;
        while (candidate.Length > 0)
        {
            var trimmed = TrimToLength(candidate, Math.Max(0, candidate.Length - 16));
            var encoded = $"{HtmlEncode(trimmed)}...";
            if (encoded.Length <= maxLength)
            {
                return encoded;
            }

            candidate = trimmed;
        }

        return string.Empty;
    }

    private static HtmlChunk TakeHtmlEncodedChunk(string rawText, int maxEncodedLength)
    {
        if (maxEncodedLength <= 0 || string.IsNullOrWhiteSpace(rawText))
        {
            return new HtmlChunk(string.Empty, 0);
        }

        var low = 1;
        var high = rawText.Length;
        var best = 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (HtmlEncode(rawText[..middle]).Length <= maxEncodedLength)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (best < rawText.Length)
        {
            var preferredSplit = rawText.LastIndexOfAny([' ', '\n', '\r', '\t'], best - 1, best);
            if (preferredSplit > 0)
            {
                best = preferredSplit;
            }
        }

        var chunk = rawText[..best].TrimEnd();
        return new HtmlChunk(HtmlEncode(chunk), best);
    }

    private static string BuildHeaderHtml(string channelName, string? channelUrl)
    {
        var headerText = HtmlEncode(channelName.Trim());
        return string.IsNullOrWhiteSpace(channelUrl)
            ? $"<b>{headerText}</b>"
            : $"<b><a href=\"{HtmlEncode(channelUrl.Trim())}\">{headerText}</a></b>";
    }

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    private sealed record HtmlChunk(string Encoded, int ConsumedLength);

    public sealed record CaptionRenderResult(string Caption, IReadOnlyList<string> OverflowMessages);
}
