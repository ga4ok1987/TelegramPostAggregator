using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IFeedService
{
    Task<IReadOnlyList<FeedItemDto>> GetPersonalFeedAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default);
}
