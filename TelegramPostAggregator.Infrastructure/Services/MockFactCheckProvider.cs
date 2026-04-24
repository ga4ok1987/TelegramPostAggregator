using System.Text.Json;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class MockFactCheckProvider : IFactCheckProvider
{
    public Task<FactCheckResultDto> FactCheckAsync(FactCheckRequest request, TelegramPost post, CancellationToken cancellationToken = default)
    {
        var seed = Math.Abs(post.ContentHash.GetHashCode());
        var score = decimal.Round((seed % 100) / 100m, 2);
        var evidence = JsonSerializer.Serialize(new
        {
            method = "mock-provider",
            normalizedHash = post.ContentHash,
            generatedAtUtc = DateTimeOffset.UtcNow
        });

        return Task.FromResult(new FactCheckResultDto(
            "MockAiVerifier",
            Guid.NewGuid().ToString("N"),
            score,
            $"Mock review completed for post {post.Id}. This is a placeholder summary ready to be replaced with a real LLM / fact-check vendor.",
            evidence));
    }
}
