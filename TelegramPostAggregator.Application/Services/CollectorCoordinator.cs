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

public sealed class CollectorCoordinator(
    ICollectorAccountRepository collectorAccountRepository,
    ITrackedChannelRepository trackedChannelRepository,
    IPostRepository postRepository,
    ITelegramCollectorGateway telegramCollectorGateway,
    ITextNormalizer textNormalizer,
    IOptions<CollectorOptions> options,
    ILogger<CollectorCoordinator> logger) : ICollectorCoordinator
{
    public async Task ProcessPendingSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await collectorAccountRepository.GetPendingAssignmentsAsync(cancellationToken);
        foreach (var assignment in assignments.Take(options.Value.SubscriptionBatchSize))
        {
            var result = await telegramCollectorGateway.EnsureJoinedAsync(assignment.CollectorAccount, assignment.Channel, cancellationToken);
            assignment.Channel.LastSubscriptionAttemptAtUtc = DateTimeOffset.UtcNow;

            if (result.Success)
            {
                assignment.JoinedAtUtc = DateTimeOffset.UtcNow;
                assignment.Status = ChannelTrackingStatus.Active;
                assignment.Channel.Status = ChannelTrackingStatus.Active;
                assignment.Channel.TelegramChannelId = result.ExternalChannelId ?? assignment.Channel.TelegramChannelId;
                assignment.Channel.ChannelName = string.IsNullOrWhiteSpace(result.DisplayName) ? assignment.Channel.ChannelName : result.DisplayName!;
                assignment.Channel.LastCollectorError = null;
            }
            else
            {
                assignment.Status = ChannelTrackingStatus.Failed;
                assignment.Channel.Status = ChannelTrackingStatus.Failed;
                assignment.Channel.LastCollectorError = result.ErrorMessage;
                assignment.LastError = result.ErrorMessage;
            }

            assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;
            assignment.Channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await collectorAccountRepository.SaveChangesAsync(cancellationToken);
        await trackedChannelRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task SynchronizePostsAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await collectorAccountRepository.GetAssignmentsForSynchronizationAsync(cancellationToken);
        foreach (var assignment in assignments.Take(options.Value.PostSyncBatchSize))
        {
            try
            {
                var posts = await telegramCollectorGateway.GetRecentPostsAsync(
                    assignment.CollectorAccount,
                    assignment.Channel,
                    assignment.LastSyncedAtUtc ?? assignment.Channel.LastPostCollectedAtUtc,
                    cancellationToken);

                var orderedPosts = posts.OrderBy(x => x.PublishedAtUtc).ToList();
                var existingPostsByMessageId = new Dictionary<long, TelegramPost>(
                    await postRepository.GetByChannelAndMessageIdsAsync(
                        assignment.ChannelId,
                        orderedPosts.Select(x => x.TelegramMessageId).ToArray(),
                        cancellationToken));

                foreach (var collectedPost in orderedPosts)
                {
                    existingPostsByMessageId.TryGetValue(collectedPost.TelegramMessageId, out var existing);
                    if (existing is not null)
                    {
                        if (NeedsRefresh(existing, collectedPost))
                        {
                            var refreshedNormalizedText = textNormalizer.Normalize(collectedPost.Text);
                            existing.PublishedAtUtc = collectedPost.PublishedAtUtc;
                            existing.AuthorSignature = collectedPost.AuthorSignature;
                            existing.RawText = collectedPost.Text;
                            existing.NormalizedText = refreshedNormalizedText;
                            existing.MediaGroupId = collectedPost.MediaGroupId;
                            existing.HasMedia = collectedPost.HasMedia;
                            existing.IsForwarded = collectedPost.IsForwarded;
                            existing.OriginalPostUrl = collectedPost.OriginalPostUrl;
                            existing.MetadataJson = collectedPost.MetadataJson;
                            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        }

                        continue;
                    }

                    var normalizedText = textNormalizer.Normalize(collectedPost.Text);

                    var post = new TelegramPost
                    {
                        ChannelId = assignment.ChannelId,
                        CollectorAccountId = assignment.CollectorAccountId,
                        TelegramMessageId = collectedPost.TelegramMessageId,
                        PublishedAtUtc = collectedPost.PublishedAtUtc,
                        AuthorSignature = collectedPost.AuthorSignature,
                        RawText = collectedPost.Text,
                        NormalizedText = normalizedText,
                        MediaGroupId = collectedPost.MediaGroupId,
                        HasMedia = collectedPost.HasMedia,
                        IsForwarded = collectedPost.IsForwarded,
                        OriginalPostUrl = collectedPost.OriginalPostUrl,
                        MetadataJson = collectedPost.MetadataJson
                    };

                    await postRepository.AddAsync(post, cancellationToken);
                    existingPostsByMessageId[post.TelegramMessageId] = post;
                }

                assignment.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                assignment.Channel.LastPostCollectedAtUtc = DateTimeOffset.UtcNow;
                assignment.Channel.Status = ChannelTrackingStatus.Active;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Synchronization failed for channel {ChannelId}", assignment.ChannelId);
                assignment.Status = ChannelTrackingStatus.Failed;
                assignment.LastError = exception.Message;
                assignment.Channel.LastCollectorError = exception.Message;
            }
        }

        await postRepository.SaveChangesAsync(cancellationToken);
        await collectorAccountRepository.SaveChangesAsync(cancellationToken);
        await trackedChannelRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool NeedsRefresh(TelegramPost existing, CollectedPostDto collectedPost)
    {
        if (existing.RawText.StartsWith("message", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(existing.OriginalPostUrl) && !string.IsNullOrWhiteSpace(collectedPost.OriginalPostUrl))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(existing.OriginalPostUrl) &&
            !string.IsNullOrWhiteSpace(collectedPost.OriginalPostUrl) &&
            !string.Equals(existing.OriginalPostUrl, collectedPost.OriginalPostUrl, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.RawText, collectedPost.Text, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.MetadataJson, collectedPost.MetadataJson, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(existing.MediaGroupId, collectedPost.MediaGroupId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
