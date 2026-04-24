namespace TelegramPostAggregator.Domain.Enums;

public enum PostSourceKind
{
    ChannelPost = 1,
    ForwardedPost = 2,
    ImportedHistoricalPost = 3
}
