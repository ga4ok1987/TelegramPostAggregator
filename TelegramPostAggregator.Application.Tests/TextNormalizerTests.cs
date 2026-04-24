using TelegramPostAggregator.Application.Services;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Normalize_PreservesUrlsAndCollapsesWhitespace()
    {
        var normalizer = new TextNormalizer();

        var normalized = normalizer.Normalize("  HTTPS://t.me/test_channel/123   Hello   WORLD  ");

        Assert.Equal("https://t.me/test_channel/123 hello world", normalized);
    }

    [Fact]
    public void ComputeHash_SameNormalizedText_ProducesStableHash()
    {
        var normalizer = new TextNormalizer();

        var first = normalizer.ComputeHash(normalizer.Normalize("Hello   World"));
        var second = normalizer.ComputeHash(normalizer.Normalize(" hello world "));

        Assert.Equal(first, second);
    }
}
