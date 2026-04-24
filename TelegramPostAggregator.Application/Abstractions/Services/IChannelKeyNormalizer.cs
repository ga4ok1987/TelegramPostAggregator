namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IChannelKeyNormalizer
{
    string Normalize(string input);
}
