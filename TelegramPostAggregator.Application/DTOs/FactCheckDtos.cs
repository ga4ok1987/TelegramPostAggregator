namespace TelegramPostAggregator.Application.DTOs;

public sealed record CreateFactCheckRequestDto(Guid PostId, long TelegramUserId, string Prompt);

public sealed record FactCheckRequestDto(
    Guid Id,
    Guid PostId,
    string Status,
    decimal? CredibilityScore,
    string? ResultSummary,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage);

public sealed record FactCheckResultDto(
    string ProviderName,
    string? ProviderRequestId,
    decimal CredibilityScore,
    string Summary,
    string SupportingEvidenceJson);
