namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface ITextNormalizer
{
    string Normalize(string? rawText);
    string ComputeHash(string normalizedText);
}
