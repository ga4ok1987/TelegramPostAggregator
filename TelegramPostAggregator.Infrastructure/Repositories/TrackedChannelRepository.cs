using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class TrackedChannelRepository(AggregatorDbContext dbContext) : ITrackedChannelRepository
{
    public Task<TrackedChannel?> GetByNormalizedKeyAsync(string normalizedKey, CancellationToken cancellationToken = default) =>
        dbContext.TrackedChannels
            .Include(x => x.CollectorAssignments)
            .FirstOrDefaultAsync(x => x.NormalizedChannelKey == normalizedKey, cancellationToken);

    public Task<TrackedChannel?> GetByTelegramChannelIdAsync(string telegramChannelId, CancellationToken cancellationToken = default) =>
        dbContext.TrackedChannels
            .Include(x => x.CollectorAssignments)
            .FirstOrDefaultAsync(x => x.TelegramChannelId == telegramChannelId, cancellationToken);

    public Task<TrackedChannel?> GetWithAssignmentsAsync(Guid channelId, CancellationToken cancellationToken = default) =>
        dbContext.TrackedChannels
            .Include(x => x.CollectorAssignments)
            .ThenInclude(x => x.CollectorAccount)
            .FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken);

    public async Task<IReadOnlyList<TrackedChannel>> GetChannelsForUserAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Where(x => x.User.TelegramUserId == telegramUserId && x.IsActive)
            .Select(x => x.Channel)
            .OrderBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrackedChannel>> GetKnownChannelsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.TrackedChannels
            .Where(x => x.Status == ChannelTrackingStatus.Active && !string.IsNullOrWhiteSpace(x.TelegramChannelId))
            .OrderBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrackedChannel>> GetChannelsByStatusAsync(ChannelTrackingStatus status, CancellationToken cancellationToken = default) =>
        await dbContext.TrackedChannels
            .Where(x => x.Status == status)
            .OrderBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(TrackedChannel channel, CancellationToken cancellationToken = default) =>
        await dbContext.TrackedChannels.AddAsync(channel, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
