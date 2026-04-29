using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ITrackedChannelRepository
{
    Task<TrackedChannel?> GetByNormalizedKeyAsync(string normalizedKey, CancellationToken cancellationToken = default);
    Task<TrackedChannel?> GetByTelegramChannelIdAsync(string telegramChannelId, CancellationToken cancellationToken = default);
    Task<TrackedChannel?> GetWithAssignmentsAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackedChannel>> GetChannelsForUserAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackedChannel>> GetKnownChannelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackedChannel>> GetChannelsByStatusAsync(ChannelTrackingStatus status, CancellationToken cancellationToken = default);
    Task AddAsync(TrackedChannel channel, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
