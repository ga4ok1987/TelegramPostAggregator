using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class PostEmbeddingService(
    IPostRepository postRepository,
    ITelegramPostEmbeddingRepository postEmbeddingRepository,
    IEmbeddingSettingsRepository embeddingSettingsRepository,
    IOpenAiApiKeyRepository openAiApiKeyRepository,
    IHttpClientFactory httpClientFactory,
    IOptions<EmbeddingOptions> embeddingOptions,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<PostEmbeddingService> logger) : IPostEmbeddingService
{
    private static readonly string[] EmptyMediaTextMarkers =
    [
        "(media post)",
        "(photo post)",
        "(video post)",
        "(animation post)",
        "(document post)",
        "(audio post)",
        "(voice message)",
        "(sticker)",
        "(video note)",
        "(no text)"
    ];

    private readonly EmbeddingOptions _embeddingOptions = embeddingOptions.Value;
    private readonly OpenAiOptions _openAiOptions = openAiOptions.Value;
    private static readonly JsonSerializerOptions EmbeddingJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!_embeddingOptions.Enabled)
        {
            return 0;
        }

        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogDebug("Post embeddings are enabled in code, but OpenAI api key is not configured.");
            return 0;
        }

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        if (!settings.IsEnabled)
        {
            return 0;
        }

        var retentionCutoffUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(settings.RetentionDays, 1));
        var expiredPending = await postRepository.GetExpiredPendingEmbeddingsAsync(
            retentionCutoffUtc,
            _embeddingOptions.CleanupBatchSize,
            cancellationToken);

        foreach (var post in expiredPending)
        {
            post.EmbeddingStatus = EmbeddingStatus.None;
            post.EmbeddingLastError = null;
            post.EmbeddingUpdatedAtUtc = DateTimeOffset.UtcNow;
            post.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (expiredPending.Count > 0)
        {
            await postRepository.SaveChangesAsync(cancellationToken);
        }

        var pendingPosts = await postRepository.GetPendingEmbeddingsBatchAsync(
            retentionCutoffUtc,
            Math.Max(_embeddingOptions.BatchSize, 1),
            cancellationToken);

        if (pendingPosts.Count == 0)
        {
            return 0;
        }

        var inputItems = pendingPosts
            .Select(post => new
            {
                Post = post,
                Text = BuildEmbeddingText(post),
                TextVersion = 1
            })
            .ToArray();

        foreach (var item in inputItems)
        {
            item.Post.EmbeddingStatus = EmbeddingStatus.Processing;
            item.Post.EmbeddingLastError = null;
            item.Post.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await postRepository.SaveChangesAsync(cancellationToken);

        try
        {
            var vectors = await GenerateEmbeddingsAsync(
                settings.Model,
                inputItems.Select(x => x.Text).ToArray(),
                apiKey,
                cancellationToken);

            var embeddedAtUtc = DateTimeOffset.UtcNow;
            for (var index = 0; index < inputItems.Length; index++)
            {
                var item = inputItems[index];
                var vector = vectors[index];
                var existing = await postEmbeddingRepository.GetByPostIdAsync(item.Post.Id, cancellationToken);
                if (existing is null)
                {
                    existing = new TelegramPostEmbedding
                    {
                        PostId = item.Post.Id
                    };

                    await postEmbeddingRepository.AddAsync(existing, cancellationToken);
                }

                existing.Model = settings.Model;
                existing.TextVersion = item.TextVersion;
                existing.NormalizedText = item.Text;
                existing.VectorJson = JsonSerializer.Serialize(vector);
                existing.Dimensions = vector.Length;
                existing.ExpiresAtUtc = item.Post.PublishedAtUtc.AddDays(Math.Max(settings.RetentionDays, 1));
                existing.UpdatedAtUtc = embeddedAtUtc;

                item.Post.EmbeddingStatus = EmbeddingStatus.Ready;
                item.Post.EmbeddingModel = settings.Model;
                item.Post.EmbeddingTextVersion = item.TextVersion;
                item.Post.EmbeddingUpdatedAtUtc = embeddedAtUtc;
                item.Post.EmbeddingLastError = null;
                item.Post.UpdatedAtUtc = embeddedAtUtc;
            }

            await postEmbeddingRepository.SaveChangesAsync(cancellationToken);
            await postRepository.SaveChangesAsync(cancellationToken);
            return inputItems.Length;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to generate embeddings for {Count} posts.", inputItems.Length);

            foreach (var item in inputItems)
            {
                item.Post.EmbeddingStatus = EmbeddingStatus.Failed;
                item.Post.EmbeddingLastError = TruncateError(exception.Message);
                item.Post.EmbeddingUpdatedAtUtc = DateTimeOffset.UtcNow;
                item.Post.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await postRepository.SaveChangesAsync(cancellationToken);
            return 0;
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var expired = await postEmbeddingRepository.GetExpiredBatchAsync(
            nowUtc,
            Math.Max(_embeddingOptions.CleanupBatchSize, 1),
            cancellationToken);

        foreach (var embedding in expired)
        {
            embedding.Post.EmbeddingStatus = EmbeddingStatus.None;
            embedding.Post.EmbeddingModel = null;
            embedding.Post.EmbeddingTextVersion = null;
            embedding.Post.EmbeddingUpdatedAtUtc = nowUtc;
            embedding.Post.EmbeddingLastError = null;
            embedding.Post.UpdatedAtUtc = nowUtc;
            postEmbeddingRepository.Remove(embedding);
        }

        if (expired.Count == 0)
        {
            return 0;
        }

        await postEmbeddingRepository.SaveChangesAsync(cancellationToken);
        await postRepository.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<float[][]> GenerateEmbeddingsAsync(string model, IReadOnlyList<string> input, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEmbeddingsUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            input
        });

        using var client = httpClientFactory.CreateClient(nameof(PostEmbeddingService));
        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI embeddings request failed with {(int)response.StatusCode}: {responseBody}");
        }

        var payload = JsonSerializer.Deserialize<EmbeddingResponse>(responseBody, EmbeddingJsonOptions);
        if (payload?.Data is null || payload.Data.Count != input.Count)
        {
            throw new InvalidOperationException("OpenAI embeddings response did not contain the expected number of vectors.");
        }

        return payload.Data
            .OrderBy(x => x.Index)
            .Select(x => x.Embedding.ToArray())
            .ToArray();
    }

    private async Task<EmbeddingSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await embeddingSettingsRepository.GetAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new EmbeddingSettings();
        await embeddingSettingsRepository.AddAsync(settings, cancellationToken);
        await embeddingSettingsRepository.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        if (await openAiApiKeyRepository.HasAnyAsync(cancellationToken))
        {
            var activeKey = await openAiApiKeyRepository.GetActiveAsync(cancellationToken);
            return activeKey?.ApiKey?.Trim();
        }

        return string.IsNullOrWhiteSpace(_openAiOptions.ApiKey)
            ? null
            : _openAiOptions.ApiKey.Trim();
    }

    private string BuildEmbeddingsUri()
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(_openAiOptions.BaseUrl)
            ? "https://api.openai.com/v1/"
            : _openAiOptions.BaseUrl.Trim();

        return normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? $"{normalizedBaseUrl}embeddings"
            : $"{normalizedBaseUrl}/embeddings";
    }

    private static string BuildEmbeddingText(TelegramPost post)
    {
        var parts = new List<string>();
        var channelName = post.Channel.ChannelName?.Trim();
        if (!string.IsNullOrWhiteSpace(channelName))
        {
            parts.Add(channelName);
        }

        var body = string.IsNullOrWhiteSpace(post.RawText) ? string.Empty : post.RawText.Trim();
        if (!string.IsNullOrWhiteSpace(body) &&
            !EmptyMediaTextMarkers.Any(marker => string.Equals(marker, body, StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add(body);
        }
        else if (post.HasMedia)
        {
            parts.Add(ResolveMediaMarker(post.MetadataJson));
        }

        if (!string.IsNullOrWhiteSpace(post.AuthorSignature))
        {
            parts.Add(post.AuthorSignature.Trim());
        }

        return string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ResolveMediaMarker(string metadataJson)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<EmbeddingMetadata>(metadataJson);
            return metadata?.MediaKind switch
            {
                "photo" => "photo post",
                "video" => "video post",
                "audio" => "audio post",
                "voice" => "voice message",
                "document" => "document post",
                "animation" => "gif post",
                "sticker" => "sticker post",
                "video_note" => "video note",
                _ => "media post"
            };
        }
        catch
        {
            return "media post";
        }
    }

    private static string TruncateError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return message.Length <= 2048 ? message : message[..2048];
    }

    private sealed class EmbeddingMetadata
    {
        public string? MediaKind { get; set; }
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingItem> Data { get; set; } = [];
    }

    private sealed class EmbeddingItem
    {
        public int Index { get; set; }
        public List<float> Embedding { get; set; } = [];
    }
}
