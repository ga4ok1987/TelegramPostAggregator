using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IUserService
{
    Task<UserDto> UpsertTelegramUserAsync(BotUserSnapshotDto snapshot, CancellationToken cancellationToken = default);
    Task<UserDto> SetPreferredLanguageAsync(long telegramUserId, string languageCode, CancellationToken cancellationToken = default);
}
