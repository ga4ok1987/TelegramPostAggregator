using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Infrastructure.Models;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramFeedDeliveryService(
    ITelegramBotGateway telegramBotGateway,
    IErrorAlertService errorAlertService,
    IImmediateDeliverySignal immediateDeliverySignal,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramFeedDeliveryService> logger) : BackgroundService
{
    private sealed record DeliveryOutcome(int DeliveredPosts, int AlbumMessages, int FallbackMessages);

    private static readonly TimeSpan AlbumStabilizationWindow = TimeSpan.FromSeconds(4);
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
                await errorAlertService.SendAsync(
                    "Feed delivery iteration failed",
                    "Telegram feed delivery loop failed.",
                    exception,
                    stoppingToken);
            }

            await immediateDeliverySignal.WaitAsync(TimeSpan.FromMilliseconds(_options.DeliveryDelayMilliseconds), stoppingToken);
        }
    }

    private async Task DeliverNewPostsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();

        var subscriptions = await subscriptionRepository.GetActiveForDeliveryAsync(_options.DeliveryBatchSize, cancellationToken);
        var deliveredPostsCount = 0;
        var deliveredAlbumCount = 0;
        var fallbackCount = 0;

        foreach (var subscription in subscriptions)
        {
            if (subscription.LastDeliveredTelegramMessageId is null)
            {
                var latestMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, cancellationToken);
                if (latestMessageId.HasValue)
                {
                    subscription.LastDeliveredTelegramMessageId = latestMessageId.Value;
                    subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                }

                continue;
            }

            var newPosts = await postRepository.GetUndeliveredForChannelAsync(
                subscription.ChannelId,
                subscription.LastDeliveredTelegramMessageId,
                _options.DeliveryPostsPerSubscription,
                cancellationToken);

            for (var index = 0; index < newPosts.Count; index++)
            {
                var post = newPosts[index];
                if (IsAlbumPostStillArriving(post))
                {
                    break;
                }

                if (TryCollectAlbum(newPosts, index, out var albumPosts))
                {
                    var albumOutcome = await SendAlbumAsync(subscription.User.TelegramUserId, albumPosts, cancellationToken);
                    subscription.LastDeliveredTelegramMessageId = albumPosts[^1].TelegramMessageId;
                    subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                    subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    deliveredPostsCount += albumOutcome.DeliveredPosts;
                    deliveredAlbumCount += albumOutcome.AlbumMessages;
                    fallbackCount += albumOutcome.FallbackMessages;
                    index += albumPosts.Count - 1;
                    continue;
                }

                var deliveryOutcome = await SendPostAsync(subscription.User.TelegramUserId, post, cancellationToken);
                subscription.LastDeliveredTelegramMessageId = post.TelegramMessageId;
                subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                deliveredPostsCount += deliveryOutcome.DeliveredPosts;
                fallbackCount += deliveryOutcome.FallbackMessages;
            }
        }

        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        if (deliveredPostsCount > 0 || deliveredAlbumCount > 0 || fallbackCount > 0)
        {
            logger.LogInformation(
                "Telegram delivery summary: subscriptions={SubscriptionCount}, posts={DeliveredPostsCount}, albums={DeliveredAlbumCount}, fallbacks={FallbackCount}",
                subscriptions.Count,
                deliveredPostsCount,
                deliveredAlbumCount,
                fallbackCount);
        }
    }

    private static bool IsAlbumPostStillArriving(Domain.Entities.TelegramPost post) =>
        !string.IsNullOrWhiteSpace(post.MediaGroupId) &&
        DateTimeOffset.UtcNow - post.PublishedAtUtc < AlbumStabilizationWindow;

    private async Task<DeliveryOutcome> SendAlbumAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count < 2)
        {
            return await SendPostAsync(chatId, posts[0], cancellationToken);
        }

        var captionSource = posts
            .Select(post => new
            {
                Post = post,
                TextLength = string.IsNullOrWhiteSpace(post.RawText) ? 0 : post.RawText.Trim().Length
            })
            .OrderByDescending(x => x.TextLength)
            .ThenBy(x => x.Post.TelegramMessageId)
            .First()
            .Post;

        var caption = TelegramPostMessageFormatter.FormatCaptionHtml(
            captionSource.Channel.ChannelName,
            captionSource.RawText,
            captionSource.OriginalPostUrl,
            TelegramChannelLinkHelper.BuildChannelUrl(captionSource.Channel.UsernameOrInviteLink));

        var items = new List<TelegramBotMediaGroupItemDto>();
        foreach (var post in posts)
        {
            var metadata = PostMediaMetadata.Deserialize(post.MetadataJson);
            if (metadata?.MediaKind is not ("photo" or "video") || !File.Exists(metadata.MediaLocalPath))
            {
                logger.LogWarning("Album {MediaGroupId} contains unsupported or missing media for post {TelegramMessageId}. Falling back to individual delivery.", post.MediaGroupId, post.TelegramMessageId);
                var fallbackCount = 0;
                foreach (var itemPost in posts)
                {
                    var result = await SendPostAsync(chatId, itemPost, cancellationToken);
                    fallbackCount += result.FallbackMessages;
                }

                return new DeliveryOutcome(posts.Count, 0, Math.Max(1, fallbackCount));
            }

            items.Add(new TelegramBotMediaGroupItemDto(
                metadata.MediaKind,
                metadata.MediaLocalPath!,
                items.Count == 0 ? caption : string.Empty,
                items.Count == 0 ? "HTML" : null));
        }

        try
        {
            await telegramBotGateway.SendMediaGroupAsync(new TelegramBotMediaGroupMessageDto(chatId, items), cancellationToken);
            return new DeliveryOutcome(posts.Count, 1, 0);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to deliver album {MediaGroupId} as media group. Falling back to individual delivery.", posts[0].MediaGroupId);
            await errorAlertService.SendAsync(
                "Album delivery failed",
                $"MediaGroupId: {posts[0].MediaGroupId}. Falling back to individual delivery.",
                exception,
                cancellationToken);

            var fallbackCount = 0;
            foreach (var post in posts)
            {
                var result = await SendPostAsync(chatId, post, cancellationToken);
                fallbackCount += result.FallbackMessages;
            }

            return new DeliveryOutcome(posts.Count, 0, Math.Max(1, fallbackCount));
        }
    }

    private static bool TryCollectAlbum(
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        int startIndex,
        out IReadOnlyList<Domain.Entities.TelegramPost> albumPosts)
    {
        var firstPost = posts[startIndex];
        if (string.IsNullOrWhiteSpace(firstPost.MediaGroupId))
        {
            albumPosts = [];
            return false;
        }

        var collected = new List<Domain.Entities.TelegramPost> { firstPost };
        for (var index = startIndex + 1; index < posts.Count; index++)
        {
            if (!string.Equals(posts[index].MediaGroupId, firstPost.MediaGroupId, StringComparison.Ordinal))
            {
                break;
            }

            collected.Add(posts[index]);
        }

        if (collected.Count < 2)
        {
            albumPosts = [];
            return false;
        }

        albumPosts = collected;
        return true;
    }

    private async Task<DeliveryOutcome> SendPostAsync(
        long chatId,
        Domain.Entities.TelegramPost post,
        CancellationToken cancellationToken)
    {
        var channelUrl = TelegramChannelLinkHelper.BuildChannelUrl(post.Channel.UsernameOrInviteLink);
        var messageParts = TelegramPostMessageFormatter.FormatMessagePartsHtml(
            post.Channel.ChannelName,
            post.RawText,
            post.OriginalPostUrl,
            channelUrl);
        var caption = TelegramPostMessageFormatter.FormatCaptionHtml(
            post.Channel.ChannelName,
            post.RawText,
            post.OriginalPostUrl,
            channelUrl);
        var metadata = PostMediaMetadata.Deserialize(post.MetadataJson);

        if (metadata?.MediaKind == "photo" && File.Exists(metadata.MediaLocalPath))
        {
            try
            {
                await telegramBotGateway.SendPhotoAsync(new TelegramBotMediaMessageDto(chatId, metadata.MediaLocalPath!, caption, "HTML"), cancellationToken);
                return new DeliveryOutcome(1, 0, 0);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to deliver photo post {TelegramMessageId} as media. Falling back to text message.", post.TelegramMessageId);
                await errorAlertService.SendAsync(
                    "Photo delivery failed",
                    $"Channel: {post.Channel.ChannelName}. MessageId: {post.TelegramMessageId}. Falling back to text.",
                    exception,
                    cancellationToken);
            }

            foreach (var messagePart in messageParts)
            {
                await telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(chatId, messagePart, true, ParseMode: "HTML"), cancellationToken);
            }

            return new DeliveryOutcome(1, 0, 1);
        }

        if (metadata?.MediaKind == "video" && File.Exists(metadata.MediaLocalPath))
        {
            try
            {
                await telegramBotGateway.SendVideoAsync(new TelegramBotMediaMessageDto(chatId, metadata.MediaLocalPath!, caption, "HTML"), cancellationToken);
                return new DeliveryOutcome(1, 0, 0);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to deliver video post {TelegramMessageId} as media. Falling back to text message.", post.TelegramMessageId);
                await errorAlertService.SendAsync(
                    "Video delivery failed",
                    $"Channel: {post.Channel.ChannelName}. MessageId: {post.TelegramMessageId}. Falling back to text.",
                    exception,
                    cancellationToken);
            }

            foreach (var messagePart in messageParts)
            {
                await telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(chatId, messagePart, true, ParseMode: "HTML"), cancellationToken);
            }

            return new DeliveryOutcome(1, 0, 1);
        }

        foreach (var messagePart in messageParts)
        {
            await telegramBotGateway.SendMessageAsync(new TelegramBotOutboundMessageDto(chatId, messagePart, true, ParseMode: "HTML"), cancellationToken);
        }

        return new DeliveryOutcome(1, 0, 0);
    }
}
