using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramFeedDeliveryService(
    ITelegramBotGateway telegramBotGateway,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    TdLibCollectorClientManager tdLibCollectorClientManager,
    ILogger<TelegramFeedDeliveryService> logger) : BackgroundService
{
    private const int MessageTextLimit = 3500;
    private const int MediaCaptionLimit = 1024;
    private const string HtmlParseMode = "HTML";
    private readonly TelegramBotOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Telegram feed delivery is disabled because BotToken is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverNewPostsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram feed delivery iteration failed.");
            }

            await Task.Delay(_options.DeliveryDelayMilliseconds, stoppingToken);
        }
    }

    private async Task DeliverNewPostsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();

        var subscriptions = await subscriptionRepository.GetActiveForDeliveryAsync(_options.DeliveryBatchSize, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            try
            {
                using var subscriptionTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                subscriptionTimeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.DeliverySubscriptionTimeLimitSeconds)));
                var subscriptionToken = subscriptionTimeoutCts.Token;

                if (subscription.LastDeliveredTelegramMessageId is null)
                {
                    var latestMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, subscriptionToken);
                    if (latestMessageId.HasValue)
                    {
                        subscription.LastDeliveredTelegramMessageId = latestMessageId.Value;
                        subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    }

                    continue;
                }

                var newPosts = await postRepository.GetUndeliveredForChannelAsync(
                    subscription.ChannelId,
                    subscription.LastDeliveredTelegramMessageId,
                    Math.Max(_options.DeliveryPostsPerSubscription * 10, 100),
                    subscriptionToken);

                if (newPosts.Count == 0)
                {
                    subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                    subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    continue;
                }

                var deliveredBatchCount = 0;
                for (var index = 0; index < newPosts.Count; index++)
                {
                    subscriptionToken.ThrowIfCancellationRequested();

                    var post = newPosts[index];
                    var groupedPosts = CollectMediaGroupPosts(newPosts, index);
                    var deliveredPosts = groupedPosts.Count > 0 ? groupedPosts : [post];
                    var checkpointPost = deliveredPosts[^1];
                    var response = await SendPostsAsync(subscription.User.TelegramUserId, deliveredPosts, subscriptionToken);

                    if (response.IsSuccessStatusCode)
                    {
                        subscription.LastDeliveredTelegramMessageId = checkpointPost.TelegramMessageId;
                        subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        deliveredBatchCount += deliveredPosts.Count;
                        index += deliveredPosts.Count - 1;

                        if (deliveredBatchCount >= _options.DeliveryPostsPerSubscription)
                        {
                            break;
                        }

                        continue;
                    }

                    var shouldContinue = await HandleDeliveryFailureAsync(response, subscription, checkpointPost, subscriptionToken);
                    if (!shouldContinue)
                    {
                        break;
                    }

                    deliveredBatchCount += deliveredPosts.Count;
                    index += deliveredPosts.Count - 1;
                    if (deliveredBatchCount >= _options.DeliveryPostsPerSubscription)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;

                logger.LogWarning(
                    "Delivery time limit reached for subscription {SubscriptionId} and chat {ChatId}. Moving it to the back of the queue.",
                    subscription.Id,
                    subscription.User.TelegramUserId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;

                logger.LogError(
                    exception,
                    "Failed to deliver posts for subscription {SubscriptionId} and chat {ChatId}. Moving it to the back of the queue.",
                    subscription.Id,
                    subscription.User.TelegramUserId);
            }
        }

        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<TelegramBotApiResultDto> SendPostsAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count > 1 && !string.IsNullOrWhiteSpace(posts[0].MediaGroupId))
        {
            var albumResponse = await SendMediaGroupAsync(chatId, posts, cancellationToken);
            if (albumResponse is not null)
            {
                if (albumResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return albumResponse;
            }
        }

        var post = posts[0];
        var message = FormatMessage(post.Channel.ChannelName, post.Channel.UsernameOrInviteLink, post.RawText, post.OriginalPostUrl, MessageTextLimit);
        var metadata = ParseMetadata(post.MetadataJson);
        var mediaCaption = FormatMessage(post.Channel.ChannelName, post.Channel.UsernameOrInviteLink, post.RawText, post.OriginalPostUrl, MediaCaptionLimit);

        if (IsIgnorablePost(post, metadata))
        {
            logger.LogInformation(
                "Ignoring Telegram service post {PostId} with content type {ContentType} for chat {ChatId}.",
                post.Id,
                metadata?.ContentType ?? "(unknown)",
                chatId);
            return new TelegramBotApiResultDto(true, HttpStatusCode.NoContent);
        }

        if (metadata?.MediaKind == "photo")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendPhotoAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, mediaCaption, "photo", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response;
            }
        }

        if (metadata?.MediaKind == "video")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendVideoAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, mediaCaption, "video", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response;
            }
        }

        return await telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(chatId, message, ParseMode: HtmlParseMode), cancellationToken);
    }

    private Task<TelegramBotApiResultDto> SendTextFallbackAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        CancellationToken cancellationToken)
    {
        var firstPost = posts[0];
        var lastPost = posts[^1];
        var originalPostUrl = posts.Count == 1 ? firstPost.OriginalPostUrl : lastPost.OriginalPostUrl ?? firstPost.OriginalPostUrl;
        var suffix = posts.Count == 1
            ? "Media omitted: file too large for bot delivery."
            : $"Album omitted: {posts.Count} media files were too large for bot delivery.";
        var fallbackText = string.IsNullOrWhiteSpace(firstPost.RawText)
            ? suffix
            : $"{firstPost.RawText.Trim()}{Environment.NewLine}{Environment.NewLine}{suffix}";
        var message = FormatMessage(firstPost.Channel.ChannelName, firstPost.Channel.UsernameOrInviteLink, fallbackText, originalPostUrl, MessageTextLimit);

        return telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(chatId, message, ParseMode: HtmlParseMode), cancellationToken);
    }

    private async Task<TelegramBotApiResultDto?> SendMediaGroupAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        CancellationToken cancellationToken)
    {
        var items = new List<TelegramBotMediaGroupItemDto>();
        for (var index = 0; index < posts.Count; index++)
        {
            var post = posts[index];
            var metadata = ParseMetadata(post.MetadataJson);
            if (metadata?.MediaKind is not ("photo" or "video"))
            {
                return null;
            }

            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(mediaLocalPath) || !File.Exists(mediaLocalPath))
            {
                return null;
            }

            items.Add(new TelegramBotMediaGroupItemDto(
                mediaLocalPath,
                metadata.MediaKind,
                index == 0 ? FormatMessage(post.Channel.ChannelName, post.Channel.UsernameOrInviteLink, post.RawText, post.OriginalPostUrl, MediaCaptionLimit) : null,
                index == 0 ? HtmlParseMode : null));
        }

        return await telegramBotGateway.SendMediaGroupAsync(new TelegramBotMediaGroupMessageDto(chatId, items), cancellationToken);
    }

    private async Task<string?> EnsureMediaLocalPathAsync(
        Domain.Entities.TelegramPost post,
        PostMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metadata.MediaLocalPath) && File.Exists(metadata.MediaLocalPath))
        {
            return metadata.MediaLocalPath;
        }

        if (metadata.MediaFileId is null)
        {
            return null;
        }

        return await tdLibCollectorClientManager.DownloadFileAndGetPathAsync(
            post.CollectorAccount,
            metadata.MediaFileId.Value,
            cancellationToken);
    }

    private static PostMetadata? ParseMetadata(string metadataJson)
    {
        try
        {
            return JsonSerializer.Deserialize<PostMetadata>(metadataJson);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsIgnorablePost(Domain.Entities.TelegramPost post, PostMetadata? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.ContentType) &&
            metadata.ContentType.StartsWith("messageGiveaway", StringComparison.Ordinal))
        {
            return true;
        }

        return post.RawText.StartsWith("(messageGiveaway", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMessage(string channelName, string? channelReference, string rawText, string? originalPostUrl, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(rawText) ? "(no text)" : rawText.Trim();
        if (text.Length > maxLength)
        {
            text = text[..Math.Max(0, maxLength - 3)] + "...";
        }

        var safeChannelName = HtmlEncoder.Default.Encode(channelName.Trim());
        var channelLink = ResolveChannelLink(channelReference, originalPostUrl);
        var header = string.IsNullOrWhiteSpace(channelLink)
            ? safeChannelName
            : $"<a href=\"{HtmlEncoder.Default.Encode(channelLink)}\">{safeChannelName}</a>";
        var safeText = HtmlEncoder.Default.Encode(text);
        var safePostUrl = string.IsNullOrWhiteSpace(originalPostUrl)
            ? null
            : HtmlEncoder.Default.Encode(originalPostUrl);

        return safePostUrl is null
            ? $"{header}{Environment.NewLine}{Environment.NewLine}{safeText}"
            : $"{header}{Environment.NewLine}{Environment.NewLine}{safeText}{Environment.NewLine}{Environment.NewLine}{safePostUrl}";
    }

    private static string? ResolveChannelLink(string? channelReference, string? originalPostUrl)
    {
        if (!string.IsNullOrWhiteSpace(channelReference))
        {
            var trimmedReference = channelReference.Trim();
            if (trimmedReference.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmedReference.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedReference;
            }

            if (trimmedReference.StartsWith('@'))
            {
                return $"https://t.me/{trimmedReference[1..]}";
            }
        }

        if (string.IsNullOrWhiteSpace(originalPostUrl))
        {
            return null;
        }

        var markerIndex = originalPostUrl.IndexOf("/c/", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return originalPostUrl;
        }

        var lastSlashIndex = originalPostUrl.LastIndexOf('/');
        return lastSlashIndex > "https://t.me/".Length
            ? originalPostUrl[..lastSlashIndex]
            : originalPostUrl;
    }

    private static IReadOnlyList<Domain.Entities.TelegramPost> CollectMediaGroupPosts(
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        int startIndex)
    {
        var mediaGroupId = posts[startIndex].MediaGroupId;
        if (string.IsNullOrWhiteSpace(mediaGroupId))
        {
            return [];
        }

        var groupedPosts = new List<Domain.Entities.TelegramPost> { posts[startIndex] };
        for (var index = startIndex + 1; index < posts.Count; index++)
        {
            if (!string.Equals(posts[index].MediaGroupId, mediaGroupId, StringComparison.Ordinal))
            {
                break;
            }

            groupedPosts.Add(posts[index]);
        }

        return groupedPosts;
    }

    private Task<bool> HandleDeliveryFailureAsync(
        TelegramBotApiResultDto response,
        Domain.Entities.UserChannelSubscription subscription,
        Domain.Entities.TelegramPost post,
        CancellationToken cancellationToken)
    {
        var responseBody = response.ResponseBody ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            subscription.LastDeliveredTelegramMessageId = post.TelegramMessageId;
            subscription.LastDeliveredAtUtc = now;
            subscription.UpdatedAtUtc = now;

            logger.LogWarning(
                "Skipping undeliverable post {PostId} for subscription {SubscriptionId} and chat {ChatId} after Telegram returned {StatusCode}. Response: {ResponseBody}",
                post.Id,
                subscription.Id,
                subscription.User.TelegramUserId,
                (int)response.StatusCode,
                responseBody);

            return Task.FromResult(true);
        }

        logger.LogError(
            "Telegram returned {StatusCode} for subscription {SubscriptionId}, chat {ChatId}, post {PostId}. Response: {ResponseBody}",
            (int)response.StatusCode,
            subscription.Id,
            subscription.User.TelegramUserId,
            post.Id,
            responseBody);

        return Task.FromResult(false);
    }

    private sealed class PostMetadata
    {
        public long ChatId { get; set; }
        public long MessageId { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string? MediaKind { get; set; }
        public int? MediaFileId { get; set; }
        public string? MediaLocalPath { get; set; }
    }
}
