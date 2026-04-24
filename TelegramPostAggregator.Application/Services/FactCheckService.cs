using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TelegramPostAggregator.Application.Services;

public sealed class FactCheckService(
    IFactCheckRequestRepository factCheckRequestRepository,
    IPostRepository postRepository,
    IAppUserRepository appUserRepository,
    IFactCheckProvider factCheckProvider,
    IOptions<FactCheckOptions> options,
    ILogger<FactCheckService> logger) : IFactCheckService
{
    public async Task<FactCheckRequestDto> QueueRequestAsync(CreateFactCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        var post = await postRepository.GetByIdAsync(request.PostId, cancellationToken)
            ?? throw new InvalidOperationException($"Post {request.PostId} was not found.");

        var user = await appUserRepository.GetByTelegramUserIdAsync(request.TelegramUserId, cancellationToken)
            ?? throw new InvalidOperationException($"User {request.TelegramUserId} was not found.");

        var entity = new FactCheckRequest
        {
            PostId = post.Id,
            RequestedByUserId = user.Id,
            Prompt = string.IsNullOrWhiteSpace(request.Prompt) ? "Assess the credibility of this Telegram post." : request.Prompt,
            Status = FactCheckStatus.Pending
        };

        await factCheckRequestRepository.AddAsync(entity, cancellationToken);
        await factCheckRequestRepository.SaveChangesAsync(cancellationToken);

        return ToDto(entity);
    }

    public async Task ProcessPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await factCheckRequestRepository.GetByStatusAsync(FactCheckStatus.Pending, options.Value.BatchSize, cancellationToken);

        foreach (var request in pending)
        {
            try
            {
                request.Status = FactCheckStatus.Processing;
                await factCheckRequestRepository.SaveChangesAsync(cancellationToken);

                var post = await postRepository.GetByIdAsync(request.PostId, cancellationToken)
                    ?? throw new InvalidOperationException($"Post {request.PostId} was not found.");

                var result = await factCheckProvider.FactCheckAsync(request, post, cancellationToken);

                request.Status = FactCheckStatus.Completed;
                request.ProviderName = result.ProviderName;
                request.ProviderRequestId = result.ProviderRequestId;
                request.CredibilityScore = result.CredibilityScore;
                request.ResultSummary = result.Summary;
                request.SupportingEvidenceJson = result.SupportingEvidenceJson;
                request.CompletedAtUtc = DateTimeOffset.UtcNow;
                request.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Fact-check failed for request {FactCheckRequestId}", request.Id);
                request.Status = FactCheckStatus.Failed;
                request.ErrorMessage = exception.Message;
                request.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await factCheckRequestRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private static FactCheckRequestDto ToDto(FactCheckRequest request) =>
        new(request.Id, request.PostId, request.Status.ToString(), request.CredibilityScore, request.ResultSummary, request.CompletedAtUtc, request.ErrorMessage);
}
