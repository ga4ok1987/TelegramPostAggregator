using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class FactCheckRequest : BaseEntity
{
    public Guid PostId { get; set; }
    public TelegramPost Post { get; set; } = null!;

    public Guid RequestedByUserId { get; set; }
    public AppUser RequestedByUser { get; set; } = null!;

    public FactCheckStatus Status { get; set; } = FactCheckStatus.Pending;
    public string Prompt { get; set; } = string.Empty;
    public string? ResultSummary { get; set; }
    public string? SupportingEvidenceJson { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderRequestId { get; set; }
    public decimal? CredibilityScore { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
