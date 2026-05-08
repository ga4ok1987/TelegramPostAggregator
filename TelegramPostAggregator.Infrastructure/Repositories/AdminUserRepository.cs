using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class AdminUserRepository(AggregatorDbContext dbContext) : IAdminUserRepository
{
    public Task<AdminUser?> GetByIdAsync(Guid adminUserId, CancellationToken cancellationToken = default) =>
        dbContext.AdminUsers.FirstOrDefaultAsync(x => x.Id == adminUserId, cancellationToken);

    public Task<AdminUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default) =>
        dbContext.AdminUsers.FirstOrDefaultAsync(x => x.NormalizedUsername == normalizedUsername, cancellationToken);

    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AdminUsers
            .OrderBy(x => x.Username)
            .ToListAsync(cancellationToken);

    public Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken = default) =>
        dbContext.AdminUsers.AddAsync(adminUser, cancellationToken).AsTask();

    public void Remove(AdminUser adminUser) =>
        dbContext.AdminUsers.Remove(adminUser);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
