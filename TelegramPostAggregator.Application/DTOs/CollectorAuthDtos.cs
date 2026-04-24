namespace TelegramPostAggregator.Application.DTOs;

public sealed record CollectorAuthStatusDto(
    Guid CollectorAccountId,
    string CollectorName,
    string Status,
    bool IsAuthorized,
    string? AuthenticationState,
    string? PasswordHint,
    string? LastError,
    DateTimeOffset? UpdatedAtUtc);

public sealed record SubmitCollectorCodeDto(string Code);

public sealed record SubmitCollectorPasswordDto(string Password);
