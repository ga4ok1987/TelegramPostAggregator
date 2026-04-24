using System.Text.Json;

namespace TelegramPostAggregator.Infrastructure.Models;

public sealed class PostMediaMetadata
{
    public long ChatId { get; set; }
    public long MessageId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string? MediaKind { get; set; }
    public string? MediaLocalPath { get; set; }

    public static string Serialize(PostMediaMetadata metadata) =>
        JsonSerializer.Serialize(metadata);

    public static PostMediaMetadata? Deserialize(string metadataJson)
    {
        try
        {
            return JsonSerializer.Deserialize<PostMediaMetadata>(metadataJson);
        }
        catch
        {
            return null;
        }
    }
}
