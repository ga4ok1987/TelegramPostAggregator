using TelegramPostAggregator.Application.Services;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class ChannelKeyNormalizerTests
{
    [Theory]
    [InlineData("https://t.me/censor_net/93075", "censor_net")]
    [InlineData("https://t.me/s/pravdagomel", "pravdagomel")]
    [InlineData("https://t.me/pravdagomel", "pravdagomel")]
    [InlineData("https://t.me/pravdagomel'", "pravdagomel")]
    [InlineData("@pravdagomel", "pravdagomel")]
    [InlineData("https://t.me/+wqKlt3h_J9FiMWVi", "+wqklt3h_j9fimwvi")]
    [InlineData("https://t.me/joinchat/AbCdEf123", "joinchat/abcdef123")]
    public void Normalize_HandlesTelegramChannelReferences(string input, string expected)
    {
        var normalizer = new ChannelKeyNormalizer();

        var normalized = normalizer.Normalize(input);

        Assert.Equal(expected, normalized);
    }
}
