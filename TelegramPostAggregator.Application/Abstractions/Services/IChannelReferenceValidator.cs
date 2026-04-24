namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IChannelReferenceValidator
{
    bool IsValid(string input);
}
