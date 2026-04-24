using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class FeedService(IPostRepository postRepository) : IFeedService
{
    public async Task<IReadOnlyList<FeedItemDto>> GetPersonalFeedAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default)
    {
        var posts = await postRepository.GetFeedForUserAsync(telegramUserId, take, skip, cancellationToken);

        return posts.Select(post => new FeedItemDto(
            post.Id,
            post.Channel.ChannelName,
            post.RawText.Length > 280 ? post.RawText[..280] : post.RawText,
            post.PublishedAtUtc,
            post.HasMedia,
            post.IsForwarded,
            post.Channel.Status.ToString())).ToList();
    }
}
