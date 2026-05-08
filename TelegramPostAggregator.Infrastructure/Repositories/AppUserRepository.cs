using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class AppUserRepository(AggregatorDbContext dbContext) : IAppUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsSplitQuery()
            .Include(x => x.ManagedChannels)
            .ThenInclude(x => x.SourceSubscriptions)
            .Include(x => x.ChannelSubscriptions)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        dbContext.Users.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);

    public async Task<IReadOnlyList<AppUser>> ListForAdminAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ManagedChannels)
            .Include(x => x.ChannelSubscriptions)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AppUser user, CancellationToken cancellationToken = default) =>
        await dbContext.Users.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
