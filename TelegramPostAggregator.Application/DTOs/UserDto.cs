namespace TelegramPostAggregator.Application.DTOs;

public sealed record UserDto(Guid Id, long TelegramUserId, string TelegramUsername, string DisplayName, string PreferredLanguageCode);
