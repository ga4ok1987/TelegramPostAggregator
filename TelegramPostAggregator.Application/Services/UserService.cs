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
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await userRepository.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto> SetMonitoringEnabledAsync(long telegramUserId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken)
            ?? throw new InvalidOperationException($"User {telegramUserId} was not found.");

        user.IsMonitoringEnabled = isEnabled;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await userRepository.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto> SetPreferredLanguageAsync(long telegramUserId, string languageCode, CancellationToken cancellationToken = default)
    {
        await userRepository.SetPreferredLanguageAsync(telegramUserId, languageCode, cancellationToken);
        await userRepository.SaveChangesAsync(cancellationToken);

        var user = await userRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken)
            ?? throw new InvalidOperationException($"User {telegramUserId} was not found.");

        return ToDto(user);
    }

    private static UserDto ToDto(AppUser user) =>
        new(user.Id, user.TelegramUserId, user.TelegramUsername, user.DisplayName, user.PreferredLanguageCode, user.IsMonitoringEnabled);
}
