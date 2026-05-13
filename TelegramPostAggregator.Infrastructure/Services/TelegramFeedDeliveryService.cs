using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Infrastructure.Options;
using TelegramPostAggregator.Infrastructure.Models;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramFeedDeliveryService(
    ITelegramBotGateway telegramBotGateway,
    IErrorAlertService errorAlertService,
    IImmediateDeliverySignal immediateDeliverySignal,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    TdLibCollectorClientManager tdLibCollectorClientManager,
    ILogger<TelegramFeedDeliveryService> logger) : BackgroundService
{
    private const int MessageTextLimit = 4096;
    private const int MediaCaptionLimit = 1024;
    private const string HtmlParseMode = "HTML";
    private const string EditedByAuthorLabel = "Відредаговано автором оригінального поста";
    private static readonly TimeSpan AlbumStabilizationWindow = TimeSpan.FromSeconds(4);
    private static readonly string[] EmptyMediaTextMarkers =
    [
        "(media post)",
        "(photo post)",
        "(video post)",
        "(animation post)",
        "(document post)",
        "(audio post)",
        "(voice message)",
        "(video note)",
        "(no text)"
    ];
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
        var managedChannelRepository = scope.ServiceProvider.GetRequiredService<IManagedChannelRepository>();
        var managedChannelSubscriptionRepository = scope.ServiceProvider.GetRequiredService<IManagedChannelSubscriptionRepository>();
        var managedChannelPostTrackingRepository = scope.ServiceProvider.GetRequiredService<IManagedChannelPostTrackingRepository>();
        var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

        var subscriptions = await subscriptionRepository.GetActiveForDeliveryAsync(int.MaxValue, cancellationToken);
        subscriptions = await FilterDirectSubscriptionsForCurrentPlanAsync(
            subscriptions,
            subscriptionRepository,
            managedChannelSubscriptionRepository,
            billingService,
            cancellationToken);
        subscriptions = subscriptions
            .OrderBy(x => x.LastDeliveredAtUtc ?? x.CreatedAtUtc)
            .Take(_options.DeliveryBatchSize)
            .ToArray();
        foreach (var subscription in subscriptions)
        {
            Domain.Entities.TelegramPost? currentPost = null;
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
                    currentPost = post;
                    if (IsAlbumPostStillArriving(post))
                    {
                        break;
                    }

                    var groupedPosts = CollectMediaGroupPosts(newPosts, index);
                    var deliveredPosts = groupedPosts.Count > 0 ? groupedPosts : [post];
                    var contentSourcePost = await ResolveContentSourcePostAsync(postRepository, deliveredPosts, subscriptionToken);
                    var checkpointPost = deliveredPosts[^1];
                    var response = await SendPostsAsync(subscription.User.TelegramUserId, deliveredPosts, contentSourcePost, subscriptionToken);

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
                await errorAlertService.SendAsync(
                    "Failed to deliver posts",
                    BuildDeliveryAlertMessage(subscription, currentPost),
                    exception,
                    cancellationToken);
            }
        }

        var managedSubscriptions = await managedChannelSubscriptionRepository.GetActiveForDeliveryAsync(int.MaxValue, cancellationToken);
        managedSubscriptions = await FilterManagedSubscriptionsForCurrentPlanAsync(
            managedSubscriptions,
            managedChannelRepository,
            subscriptionRepository,
            managedChannelSubscriptionRepository,
            billingService,
            cancellationToken);
        managedSubscriptions = managedSubscriptions
            .OrderBy(x => x.LastDeliveredAtUtc ?? x.CreatedAtUtc)
            .Take(_options.DeliveryBatchSize)
            .ToArray();
        foreach (var subscription in managedSubscriptions)
        {
            Domain.Entities.TelegramPost? currentPost = null;
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
                    currentPost = post;
                    if (IsAlbumPostStillArriving(post))
                    {
                        break;
                    }

                    var groupedPosts = CollectMediaGroupPosts(newPosts, index);
                    var deliveredPosts = groupedPosts.Count > 0 ? groupedPosts : [post];
                    var contentSourcePost = await ResolveContentSourcePostAsync(postRepository, deliveredPosts, subscriptionToken);
                    var checkpointPost = deliveredPosts[^1];
                    var response = await SendPostsAsync(subscription.ManagedChannel.TelegramChatId, deliveredPosts, contentSourcePost, subscriptionToken);

                    if (response.IsSuccessStatusCode)
                    {
                        subscription.LastDeliveredTelegramMessageId = checkpointPost.TelegramMessageId;
                        subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        await TrackManagedPostDeliveryAsync(
                            subscription,
                            deliveredPosts,
                            response.PrimaryMessageId,
                            managedChannelPostTrackingRepository,
                            subscriptionToken);
                        deliveredBatchCount += deliveredPosts.Count;
                        index += deliveredPosts.Count - 1;

                        if (deliveredBatchCount >= _options.DeliveryPostsPerSubscription)
                        {
                            break;
                        }

                        continue;
                    }

                    var shouldContinue = await HandleManagedChannelDeliveryFailureAsync(response, subscription, checkpointPost, subscriptionToken);
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
                    "Delivery time limit reached for managed channel subscription {SubscriptionId} and chat {ChatId}. Moving it to the back of the queue.",
                    subscription.Id,
                    subscription.ManagedChannel.TelegramChatId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                subscription.LastDeliveredAtUtc = DateTimeOffset.UtcNow;
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;

                logger.LogError(
                    exception,
                    "Failed to deliver posts for managed channel subscription {SubscriptionId} and chat {ChatId}. Moving it to the back of the queue.",
                    subscription.Id,
                    subscription.ManagedChannel.TelegramChatId);
                await errorAlertService.SendAsync(
                    "Failed to deliver posts to managed channel",
                    BuildDeliveryAlertMessage(subscription, currentPost),
                    exception,
                    cancellationToken);
            }
        }

        var pendingEditedDeliveries = await managedChannelPostTrackingRepository.GetPendingAsync(_options.DeliveryBatchSize, cancellationToken);
        foreach (var tracking in pendingEditedDeliveries)
        {
            try
            {
                if (!IsEligibleForEditedDelivery(tracking))
                {
                    tracking.PendingEditedAtUtc = null;
                    tracking.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    continue;
                }

                var posts = string.IsNullOrWhiteSpace(tracking.Post.MediaGroupId)
                    ? new[] { tracking.Post }
                    : await postRepository.GetByChannelAndMediaGroupIdAsync(tracking.Post.ChannelId, tracking.Post.MediaGroupId, cancellationToken);
                var contentSourcePost = await ResolveContentSourcePostAsync(postRepository, posts, cancellationToken);
                var previousDeliveredUrl = BuildManagedChannelMessageUrl(tracking.ManagedChannel, tracking.LastDeliveredMessageId);
                var editedBody = BuildEditedDeliveryBody(contentSourcePost.RawText, previousDeliveredUrl);
                var response = await SendPostsAsync(
                    tracking.ManagedChannel.TelegramChatId,
                    posts,
                    contentSourcePost,
                    cancellationToken,
                    editedBody,
                    contentSourcePost.OriginalPostUrl);

                if (response.IsSuccessStatusCode)
                {
                    var deliveredAtUtc = DateTimeOffset.UtcNow;
                    var deliveredMessageId = response.PrimaryMessageId ?? tracking.LastDeliveredMessageId;
                    var relatedStates = await managedChannelPostTrackingRepository.GetBySubscriptionAndPostIdsAsync(
                        tracking.ManagedChannelSubscriptionId,
                        posts.Select(x => x.Id).ToArray(),
                        cancellationToken);

                    foreach (var relatedState in relatedStates)
                    {
                        relatedState.LastDeliveredMessageId = deliveredMessageId;
                        relatedState.LastDeliveredAtUtc = deliveredAtUtc;
                        relatedState.PendingEditedAtUtc = null;
                        relatedState.LastProcessedEditedAtUtc = tracking.PendingEditedAtUtc;
                        relatedState.UpdatedAtUtc = deliveredAtUtc;
                    }

                    continue;
                }

                var shouldKeepPending = await HandleManagedChannelEditedDeliveryFailureAsync(
                    response,
                    tracking,
                    tracking.Post,
                    cancellationToken);
                if (!shouldKeepPending)
                {
                    tracking.LastProcessedEditedAtUtc = tracking.PendingEditedAtUtc;
                    tracking.PendingEditedAtUtc = null;
                    tracking.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to redeliver an edited post for managed channel subscription {SubscriptionId} and chat {ChatId}.",
                    tracking.ManagedChannelSubscriptionId,
                    tracking.ManagedChannel.TelegramChatId);
                await errorAlertService.SendAsync(
                    "Failed to redeliver edited post",
                    BuildEditedDeliveryAlertMessage(tracking, tracking.Post),
                    exception,
                    cancellationToken);
                tracking.LastProcessedEditedAtUtc = tracking.PendingEditedAtUtc;
                tracking.PendingEditedAtUtc = null;
                tracking.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        try
        {
            await subscriptionRepository.SaveChangesAsync(cancellationToken);
            await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
            await managedChannelPostTrackingRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Feed delivery state save hit a concurrency race. The iteration will continue and reconcile on the next pass.");
        }
    }

    private static async Task<IReadOnlyList<Domain.Entities.UserChannelSubscription>> FilterDirectSubscriptionsForCurrentPlanAsync(
        IReadOnlyList<Domain.Entities.UserChannelSubscription> activeSubscriptions,
        ISubscriptionRepository subscriptionRepository,
        IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
        IBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (activeSubscriptions.Count == 0)
        {
            return activeSubscriptions;
        }

        var groupedByUser = activeSubscriptions.GroupBy(x => x.User.TelegramUserId);
        var filtered = new List<Domain.Entities.UserChannelSubscription>();

        foreach (var group in groupedByUser)
        {
            var allowedChannelIds = await ResolveAllowedChannelIdsAsync(
                group.Key,
                subscriptionRepository,
                managedChannelSubscriptionRepository,
                billingService,
                cancellationToken);

            filtered.AddRange(group.Where(x => allowedChannelIds.Contains(x.ChannelId)));
        }

        return filtered;
    }

    private static async Task<IReadOnlyList<Domain.Entities.ManagedChannelSubscription>> FilterManagedSubscriptionsForCurrentPlanAsync(
        IReadOnlyList<Domain.Entities.ManagedChannelSubscription> activeSubscriptions,
        IManagedChannelRepository managedChannelRepository,
        ISubscriptionRepository subscriptionRepository,
        IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
        IBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (activeSubscriptions.Count == 0)
        {
            return activeSubscriptions;
        }

        var groupedByUser = activeSubscriptions.GroupBy(x => x.ManagedChannel.User.TelegramUserId);
        var filtered = new List<Domain.Entities.ManagedChannelSubscription>();

        foreach (var group in groupedByUser)
        {
            var allowedChannelIds = await ResolveAllowedChannelIdsAsync(
                group.Key,
                subscriptionRepository,
                managedChannelSubscriptionRepository,
                billingService,
                cancellationToken);
            var usage = await billingService.GetSubscriptionUsageAsync(group.Key, cancellationToken);
            var managedChannels = await managedChannelRepository.GetByUserTelegramIdAsync(group.Key, cancellationToken);
            var allowedManagedChannelIds = managedChannels
                .Where(x => x.IsActive)
                .Take(Math.Max(usage.ManagedChannelLimit, 0))
                .Select(x => x.Id)
                .ToHashSet();

            filtered.AddRange(group.Where(x =>
                allowedChannelIds.Contains(x.ChannelId) &&
                allowedManagedChannelIds.Contains(x.ManagedChannelId)));
        }

        return filtered;
    }

    private static async Task<HashSet<Guid>> ResolveAllowedChannelIdsAsync(
        long telegramUserId,
        ISubscriptionRepository subscriptionRepository,
        IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
        IBillingService billingService,
        CancellationToken cancellationToken)
    {
        var usage = await billingService.GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
        var directSubscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedSubscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);

        var orderedChannels = directSubscriptions
            .Select(x => new { x.ChannelId, x.CreatedAtUtc })
            .Concat(managedSubscriptions.Select(x => new { x.ChannelId, x.CreatedAtUtc }))
            .GroupBy(x => x.ChannelId)
            .Select(group => new
            {
                ChannelId = group.Key,
                FirstSeenAtUtc = group.Min(x => x.CreatedAtUtc)
            })
            .OrderBy(x => x.FirstSeenAtUtc)
            .ThenBy(x => x.ChannelId)
            .Take(Math.Max(usage.ChannelLimit, 0))
            .Select(x => x.ChannelId);

        return orderedChannels.ToHashSet();
    }

    private async Task<TelegramBotApiResultDto> SendPostsAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        Domain.Entities.TelegramPost contentSourcePost,
        CancellationToken cancellationToken,
        string? rawTextOverride = null,
        string? originalPostUrlOverride = null)
    {
        var primaryPost = posts[0];
        var effectiveRawText = rawTextOverride ?? contentSourcePost.RawText;
        var effectiveOriginalPostUrl = originalPostUrlOverride ?? contentSourcePost.OriginalPostUrl;
        var channelUrl = ResolveChannelLink(contentSourcePost.Channel.UsernameOrInviteLink, effectiveOriginalPostUrl);
        var captionRender = TelegramPostMessageFormatter.FormatMediaDeliveryHtml(
            contentSourcePost.Channel.ChannelName,
            effectiveRawText,
            effectiveOriginalPostUrl,
            channelUrl);

        if (posts.Count > 1 && !string.IsNullOrWhiteSpace(posts[0].MediaGroupId))
        {
            var albumResponse = await SendMediaGroupAsync(chatId, posts, captionRender.Caption, cancellationToken);
            if (albumResponse is not null)
            {
                if (albumResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return albumResponse.IsSuccessStatusCode
                    ? CombinePrimaryResult(albumResponse, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : albumResponse;
            }
        }

        var post = primaryPost;
        var messageParts = TelegramPostMessageFormatter.FormatMessagePartsHtml(
            contentSourcePost.Channel.ChannelName,
            effectiveRawText,
            effectiveOriginalPostUrl,
            channelUrl);
        var metadata = ParseMetadata(post.MetadataJson);

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
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "photo", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "video")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendVideoAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "video", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "audio")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendAudioAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "audio", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "voice")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendVoiceAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "voice", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "document")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendDocumentAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "document", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "animation")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendAnimationAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, captionRender.Caption, "animation", HtmlParseMode),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, captionRender.OverflowMessages, cancellationToken))
                    : response;
            }
        }

        if (metadata?.MediaKind == "video_note")
        {
            var mediaLocalPath = await EnsureMediaLocalPathAsync(post, metadata, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mediaLocalPath) && File.Exists(mediaLocalPath))
            {
                var response = await telegramBotGateway.SendVideoNoteAsync(
                    new TelegramBotMediaMessageDto(chatId, mediaLocalPath, string.Empty, "video_note", null),
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return await SendTextFallbackAsync(chatId, posts, cancellationToken);
                }

                return response.IsSuccessStatusCode
                    ? CombinePrimaryResult(response, await SendOverflowMessagesAsync(chatId, messageParts, cancellationToken))
                    : response;
            }
        }

        return await SendOverflowMessagesAsync(chatId, messageParts, cancellationToken);
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
        var messages = TelegramPostMessageFormatter.FormatMessagePartsHtml(
            firstPost.Channel.ChannelName,
            fallbackText,
            originalPostUrl,
            ResolveChannelLink(firstPost.Channel.UsernameOrInviteLink, originalPostUrl));

        return SendOverflowMessagesAsync(chatId, messages, cancellationToken);
    }

    private async Task<TelegramBotApiResultDto?> SendMediaGroupAsync(
        long chatId,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        string caption,
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
                index == 0 ? caption : null,
                index == 0 ? HtmlParseMode : null));
        }

        return await telegramBotGateway.SendMediaGroupAsync(new TelegramBotMediaGroupMessageDto(chatId, items), cancellationToken);
    }

    private async Task<TelegramBotApiResultDto> SendOverflowMessagesAsync(
        long chatId,
        IReadOnlyList<string> overflowMessages,
        CancellationToken cancellationToken)
    {
        long? firstMessageId = null;
        foreach (var overflowMessage in overflowMessages)
        {
            var response = await telegramBotGateway.SendMessageAsync(
                new TelegramBotOutboundMessageDto(chatId, overflowMessage, ParseMode: HtmlParseMode),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return response;
            }

            firstMessageId ??= response.PrimaryMessageId;
        }

        return new TelegramBotApiResultDto(true, HttpStatusCode.OK, PrimaryMessageId: firstMessageId);
    }

    private static TelegramBotApiResultDto CombinePrimaryResult(TelegramBotApiResultDto primary, TelegramBotApiResultDto tail) =>
        new(
            primary.IsSuccessStatusCode && tail.IsSuccessStatusCode,
            tail.StatusCode,
            tail.ResponseBody,
            primary.PrimaryMessageId ?? tail.PrimaryMessageId);

    private async Task<string?> EnsureMediaLocalPathAsync(
        Domain.Entities.TelegramPost post,
        PostMediaMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metadata.MediaLocalPath) && File.Exists(metadata.MediaLocalPath))
        {
            return metadata.MediaLocalPath;
        }

        if (await TryDownloadStoredMediaAsync(post, metadata, cancellationToken) is { } downloadedPath)
        {
            return downloadedPath;
        }

        return await tdLibCollectorClientManager.DownloadMessageMediaAndGetPathAsync(
            post.CollectorAccount,
            metadata.ChatId,
            metadata.MessageId,
            metadata.MediaKind,
            cancellationToken);
    }

    private async Task TrackManagedPostDeliveryAsync(
        Domain.Entities.ManagedChannelSubscription subscription,
        IReadOnlyList<Domain.Entities.TelegramPost> deliveredPosts,
        long? deliveredMessageId,
        IManagedChannelPostTrackingRepository trackingRepository,
        CancellationToken cancellationToken)
    {
        if (!deliveredMessageId.HasValue ||
            !subscription.ManagedChannel.TrackPostEdits ||
            !subscription.ManagedChannel.TrackPostEditsEnabledAtUtc.HasValue)
        {
            return;
        }

        var deliveredAtUtc = DateTimeOffset.UtcNow;
        if (deliveredAtUtc < subscription.ManagedChannel.TrackPostEditsEnabledAtUtc.Value)
        {
            return;
        }

        foreach (var post in deliveredPosts)
        {
            var trackUntilUtc = post.PublishedAtUtc.AddHours(24);
            if (trackUntilUtc <= deliveredAtUtc)
            {
                continue;
            }

            var existing = await trackingRepository.GetAsync(subscription.Id, post.Id, cancellationToken);
            if (existing is null)
            {
                await trackingRepository.AddAsync(new Domain.Entities.ManagedChannelPostTracking
                {
                    ManagedChannelId = subscription.ManagedChannelId,
                    ManagedChannelSubscriptionId = subscription.Id,
                    PostId = post.Id,
                    LastDeliveredMessageId = deliveredMessageId.Value,
                    LastDeliveredAtUtc = deliveredAtUtc,
                    TrackEditsUntilUtc = trackUntilUtc
                }, cancellationToken);
                continue;
            }

            existing.LastDeliveredMessageId = deliveredMessageId.Value;
            existing.LastDeliveredAtUtc = deliveredAtUtc;
            existing.TrackEditsUntilUtc = trackUntilUtc;
            existing.UpdatedAtUtc = deliveredAtUtc;
        }
    }

    private static bool IsEligibleForEditedDelivery(Domain.Entities.ManagedChannelPostTracking tracking)
    {
        if (!tracking.PendingEditedAtUtc.HasValue ||
            !tracking.ManagedChannel.TrackPostEdits ||
            !tracking.ManagedChannel.TrackPostEditsEnabledAtUtc.HasValue)
        {
            return false;
        }

        return tracking.ManagedChannel.IsActive &&
               tracking.ManagedChannelSubscription.IsActive &&
               tracking.LastDeliveredAtUtc >= tracking.ManagedChannel.TrackPostEditsEnabledAtUtc.Value &&
               tracking.TrackEditsUntilUtc > DateTimeOffset.UtcNow;
    }

    private static string BuildEditedDeliveryBody(string rawText, string? previousDeliveredUrl)
    {
        var parts = new List<string> { EditedByAuthorLabel };
        var normalizedBody = string.IsNullOrWhiteSpace(rawText) ? string.Empty : rawText.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedBody))
        {
            parts.Add(normalizedBody);
        }

        if (!string.IsNullOrWhiteSpace(previousDeliveredUrl))
        {
            parts.Add($"Попередня версія:{Environment.NewLine}{previousDeliveredUrl.Trim()}");
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private static string? BuildManagedChannelMessageUrl(Domain.Entities.ManagedChannel managedChannel, long messageId)
    {
        if (!string.IsNullOrWhiteSpace(managedChannel.Username))
        {
            return $"https://t.me/{managedChannel.Username.TrimStart('@')}/{messageId}";
        }

        var chatId = managedChannel.TelegramChatId.ToString();
        if (chatId.StartsWith("-100", StringComparison.Ordinal))
        {
            return $"https://t.me/c/{chatId[4..]}/{messageId}";
        }

        return null;
    }

    private static PostMediaMetadata? ParseMetadata(string metadataJson) =>
        PostMediaMetadata.Deserialize(metadataJson);

    private static bool IsIgnorablePost(Domain.Entities.TelegramPost post, PostMediaMetadata? metadata)
    {
        if (TelegramContentClassifier.IsIgnorableContentType(metadata?.ContentType))
        {
            return true;
        }

        return post.RawText.StartsWith("(messageGiveaway", StringComparison.OrdinalIgnoreCase) ||
               post.RawText.StartsWith("(messagePinMessage", StringComparison.OrdinalIgnoreCase);
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

    private static bool HasMeaningfulText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var normalized = rawText.Trim();
        return !EmptyMediaTextMarkers.Any(marker => string.Equals(normalized, marker, StringComparison.OrdinalIgnoreCase));
    }

    private static Domain.Entities.TelegramPost SelectContentSourcePost(IReadOnlyList<Domain.Entities.TelegramPost> posts) =>
        posts.FirstOrDefault(post => HasMeaningfulText(post.RawText)) ?? posts[0];

    private static async Task<Domain.Entities.TelegramPost> ResolveContentSourcePostAsync(
        IPostRepository postRepository,
        IReadOnlyList<Domain.Entities.TelegramPost> posts,
        CancellationToken cancellationToken)
    {
        var primaryPost = posts[0];
        if (string.IsNullOrWhiteSpace(primaryPost.MediaGroupId))
        {
            return primaryPost;
        }

        var fullAlbumPosts = await postRepository.GetByChannelAndMediaGroupIdAsync(
            primaryPost.ChannelId,
            primaryPost.MediaGroupId,
            cancellationToken);

        return fullAlbumPosts.Count == 0
            ? SelectContentSourcePost(posts)
            : SelectContentSourcePost(fullAlbumPosts);
    }

    private static bool IsAlbumPostStillArriving(Domain.Entities.TelegramPost post) =>
        !string.IsNullOrWhiteSpace(post.MediaGroupId) &&
        DateTimeOffset.UtcNow - post.PublishedAtUtc < AlbumStabilizationWindow;

    private async Task<bool> HandleDeliveryFailureAsync(
        TelegramBotApiResultDto response,
        Domain.Entities.UserChannelSubscription subscription,
        Domain.Entities.TelegramPost post,
        CancellationToken cancellationToken)
    {
        var responseBody = response.ResponseBody ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        if (IsUnrecoverableClientError(response.StatusCode, responseBody))
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
            return true;
        }

        if (TryGetRetryDelay(response, out var retryDelay))
        {
            subscription.LastDeliveredAtUtc = now;
            subscription.UpdatedAtUtc = now;

            logger.LogWarning(
                "Telegram rate-limited delivery for subscription {SubscriptionId}, chat {ChatId}. Respecting retry_after={RetryAfterSeconds}s.",
                subscription.Id,
                subscription.User.TelegramUserId,
                retryDelay.TotalSeconds);
            await Task.Delay(retryDelay, cancellationToken);
            return false;
        }

        logger.LogError(
            "Telegram returned {StatusCode} for subscription {SubscriptionId}, chat {ChatId}, post {PostId}. Response: {ResponseBody}",
            (int)response.StatusCode,
            subscription.Id,
            subscription.User.TelegramUserId,
            post.Id,
            responseBody);

        await errorAlertService.SendAsync(
            "Telegram delivery failed",
            BuildDeliveryAlertMessage(subscription, post, (int)response.StatusCode, responseBody),
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> HandleManagedChannelDeliveryFailureAsync(
        TelegramBotApiResultDto response,
        Domain.Entities.ManagedChannelSubscription subscription,
        Domain.Entities.TelegramPost post,
        CancellationToken cancellationToken)
    {
        var responseBody = response.ResponseBody ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        if (IsUnrecoverableClientError(response.StatusCode, responseBody))
        {
            subscription.LastDeliveredTelegramMessageId = post.TelegramMessageId;
            subscription.LastDeliveredAtUtc = now;
            subscription.UpdatedAtUtc = now;

            logger.LogWarning(
                "Skipping undeliverable post {PostId} for managed channel subscription {SubscriptionId} and chat {ChatId} after Telegram returned {StatusCode}. Response: {ResponseBody}",
                post.Id,
                subscription.Id,
                subscription.ManagedChannel.TelegramChatId,
                (int)response.StatusCode,
                responseBody);
            return true;
        }

        if (TryGetRetryDelay(response, out var retryDelay))
        {
            subscription.LastDeliveredAtUtc = now;
            subscription.UpdatedAtUtc = now;

            logger.LogWarning(
                "Telegram rate-limited managed channel delivery for subscription {SubscriptionId}, chat {ChatId}. Respecting retry_after={RetryAfterSeconds}s.",
                subscription.Id,
                subscription.ManagedChannel.TelegramChatId,
                retryDelay.TotalSeconds);
            await Task.Delay(retryDelay, cancellationToken);
            return false;
        }

        logger.LogError(
            "Telegram returned {StatusCode} for managed channel subscription {SubscriptionId}, chat {ChatId}, post {PostId}. Response: {ResponseBody}",
            (int)response.StatusCode,
            subscription.Id,
            subscription.ManagedChannel.TelegramChatId,
            post.Id,
            responseBody);

        await errorAlertService.SendAsync(
            "Telegram delivery failed",
            BuildDeliveryAlertMessage(subscription, post, (int)response.StatusCode, responseBody),
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> HandleManagedChannelEditedDeliveryFailureAsync(
        TelegramBotApiResultDto response,
        Domain.Entities.ManagedChannelPostTracking tracking,
        Domain.Entities.TelegramPost post,
        CancellationToken cancellationToken)
    {
        var responseBody = response.ResponseBody ?? string.Empty;

        if (TryGetRetryDelay(response, out var retryDelay))
        {
            logger.LogWarning(
                "Telegram rate-limited edited delivery for managed channel subscription {SubscriptionId}, chat {ChatId}. Respecting retry_after={RetryAfterSeconds}s.",
                tracking.ManagedChannelSubscriptionId,
                tracking.ManagedChannel.TelegramChatId,
                retryDelay.TotalSeconds);
            await Task.Delay(retryDelay, cancellationToken);
            return true;
        }

        logger.LogError(
            "Telegram returned {StatusCode} for edited managed channel delivery {SubscriptionId}, chat {ChatId}, post {PostId}. Response: {ResponseBody}",
            (int)response.StatusCode,
            tracking.ManagedChannelSubscriptionId,
            tracking.ManagedChannel.TelegramChatId,
            post.Id,
            responseBody);

        await errorAlertService.SendAsync(
            "Telegram edited delivery failed",
            BuildEditedDeliveryAlertMessage(tracking, post, (int)response.StatusCode, responseBody),
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task<string?> TryDownloadStoredMediaAsync(
        Domain.Entities.TelegramPost post,
        PostMediaMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.MediaFileId is null)
        {
            return null;
        }

        return await tdLibCollectorClientManager.DownloadFileAndGetPathAsync(
            post.CollectorAccount,
            metadata.MediaFileId.Value,
            cancellationToken);
    }

    private static string BuildDeliveryAlertMessage(
        Domain.Entities.UserChannelSubscription subscription,
        Domain.Entities.TelegramPost? post,
        int? statusCode = null,
        string? responseBody = null)
    {
        var lines = new List<string>
        {
            $"SubscriptionId: {subscription.Id}",
            $"ChatId: {subscription.User.TelegramUserId}"
        };

        if (post is not null)
        {
            lines.Add($"PostId: {post.Id}");
            lines.Add($"TelegramMessageId: {post.TelegramMessageId}");

            if (!string.IsNullOrWhiteSpace(post.OriginalPostUrl))
            {
                lines.Add($"OriginalPostUrl: {post.OriginalPostUrl}");
            }
        }

        if (statusCode.HasValue)
        {
            lines.Add($"StatusCode: {statusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            lines.Add($"Response: {responseBody}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildDeliveryAlertMessage(
        Domain.Entities.ManagedChannelSubscription subscription,
        Domain.Entities.TelegramPost? post,
        int? statusCode = null,
        string? responseBody = null)
    {
        var lines = new List<string>
        {
            $"ManagedChannelSubscriptionId: {subscription.Id}",
            $"ManagedChannelId: {subscription.ManagedChannelId}",
            $"ChatId: {subscription.ManagedChannel.TelegramChatId}"
        };

        if (post is not null)
        {
            lines.Add($"PostId: {post.Id}");
            lines.Add($"TelegramMessageId: {post.TelegramMessageId}");

            if (!string.IsNullOrWhiteSpace(post.OriginalPostUrl))
            {
                lines.Add($"OriginalPostUrl: {post.OriginalPostUrl}");
            }
        }

        if (statusCode.HasValue)
        {
            lines.Add($"StatusCode: {statusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            lines.Add($"Response: {responseBody}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildEditedDeliveryAlertMessage(
        Domain.Entities.ManagedChannelPostTracking tracking,
        Domain.Entities.TelegramPost? post,
        int? statusCode = null,
        string? responseBody = null)
    {
        var lines = new List<string>
        {
            $"ManagedChannelSubscriptionId: {tracking.ManagedChannelSubscriptionId}",
            $"ManagedChannelId: {tracking.ManagedChannelId}",
            $"ChatId: {tracking.ManagedChannel.TelegramChatId}"
        };

        if (post is not null)
        {
            lines.Add($"PostId: {post.Id}");
            lines.Add($"TelegramMessageId: {post.TelegramMessageId}");

            if (!string.IsNullOrWhiteSpace(post.OriginalPostUrl))
            {
                lines.Add($"OriginalPostUrl: {post.OriginalPostUrl}");
            }
        }

        if (statusCode.HasValue)
        {
            lines.Add($"StatusCode: {statusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            lines.Add($"Response: {responseBody}");
        }

        return string.Join('\n', lines);
    }

    private static bool IsUnrecoverableClientError(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return true;
        }

        if (statusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var body = responseBody.ToLowerInvariant();
        return body.Contains("bot was blocked by the user", StringComparison.Ordinal) ||
               body.Contains("user is deactivated", StringComparison.Ordinal) ||
               body.Contains("chat not found", StringComparison.Ordinal) ||
               body.Contains("group chat was upgraded", StringComparison.Ordinal) ||
               body.Contains("have no rights to send", StringComparison.Ordinal);
    }

    private static bool TryGetRetryDelay(TelegramBotApiResultDto response, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.Zero;
        if (response.StatusCode != HttpStatusCode.TooManyRequests || string.IsNullOrWhiteSpace(response.ResponseBody))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(response.ResponseBody);
            if (document.RootElement.TryGetProperty("parameters", out var parameters) &&
                parameters.TryGetProperty("retry_after", out var retryAfterElement) &&
                retryAfterElement.TryGetInt32(out var retryAfterSeconds) &&
                retryAfterSeconds > 0)
            {
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryAfterSeconds, 60));
                return true;
            }
        }
        catch
        {
            // Fall back to a short cooldown below.
        }

        retryDelay = TimeSpan.FromSeconds(3);
        return true;
    }

}
