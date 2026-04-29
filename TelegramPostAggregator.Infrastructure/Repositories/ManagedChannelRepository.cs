using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class ManagedChannelRepository(AggregatorDbContext dbContext) : IManagedChannelRepository
{
    public Task<ManagedChannel?> GetAsync(Guid userId, Guid managedChannelId, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannels.FirstOrDefaultAsync(
            x => x.UserId == userId && x.Id == managedChannelId,
            cancellationToken);

    public Task<ManagedChannel?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannels
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TelegramChatId == telegramChatId, cancellationToken);

    public Task<ManagedChannel?> GetByTelegramChatIdAsync(Guid userId, long telegramChatId, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannels.FirstOrDefaultAsync(
            x => x.UserId == userId && x.TelegramChatId == telegramChatId,
            cancellationToken);

    public async Task<IReadOnlyList<ManagedChannel>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannels
            .Include(x => x.User)
            .Where(x => x.User.TelegramUserId == telegramUserId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.ChannelName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ManagedChannel managedChannel, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannels.AddAsync(managedChannel, cancellationToken);

    public void Remove(ManagedChannel managedChannel) =>
        dbContext.ManagedChannels.Remove(managedChannel);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
