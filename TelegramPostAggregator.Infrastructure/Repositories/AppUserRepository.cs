using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class AppUserRepository(AggregatorDbContext dbContext) : IAppUserRepository
{
    public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        dbContext.Users.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, cancellationToken);

    public async Task SetPreferredLanguageAsync(long telegramUserId, string languageCode, CancellationToken cancellationToken = default)
    {
        var user = await GetByTelegramUserIdAsync(telegramUserId, cancellationToken)
            ?? throw new InvalidOperationException($"User {telegramUserId} was not found.");

        user.PreferredLanguageCode = languageCode;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public async Task AddAsync(AppUser user, CancellationToken cancellationToken = default) =>
        await dbContext.Users.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
