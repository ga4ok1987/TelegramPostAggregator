using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

public sealed class UserService(IAppUserRepository userRepository) : IUserService
{
    public async Task<UserDto> UpsertTelegramUserAsync(BotUserSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByTelegramUserIdAsync(snapshot.TelegramUserId, cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                TelegramUserId = snapshot.TelegramUserId,
                TelegramUsername = snapshot.TelegramUsername,
                DisplayName = snapshot.DisplayName,
                PreferredLanguageCode = string.IsNullOrWhiteSpace(snapshot.LanguageCode) ? "en" : snapshot.LanguageCode
            };

            await userRepository.AddAsync(user, cancellationToken);
        }
        else
        {
            user.TelegramUsername = snapshot.TelegramUsername;
            user.DisplayName = snapshot.DisplayName;
            user.PreferredLanguageCode = string.IsNullOrWhiteSpace(snapshot.LanguageCode) ? user.PreferredLanguageCode : snapshot.LanguageCode!;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await userRepository.SaveChangesAsync(cancellationToken);
        return new UserDto(user.Id, user.TelegramUserId, user.TelegramUsername, user.DisplayName, user.PreferredLanguageCode);
    }
}
