using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface ITelegramMiniAppAuthService
{
    Task<MiniAppAuthResultDto> AuthenticateAsync(string? initData, CancellationToken cancellationToken = default);
}
