namespace TelegramPostAggregator.Application.Exceptions;

public sealed class TrackedChannelUnavailableException(string channelReference, Exception innerException)
    : Exception($"Tracked channel is no longer accessible: {channelReference}", innerException)
{
    public string ChannelReference { get; } = channelReference;
}
