using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services.Monitoring;

public sealed class TelegramMiniAppAuthService(
    IOptions<MiniAppOptions> miniAppOptions,
    IOptions<TelegramBotOptions> telegramBotOptions) : ITelegramMiniAppAuthService
{
    private const string HashKey = "hash";
    private const string SignatureKey = "signature";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<MiniAppAuthResultDto> AuthenticateAsync(string? initData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(initData))
        {
            return Task.FromResult(Fail("Open this screen from the Telegram bot."));
        }

        var botToken = telegramBotOptions.Value.BotToken;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return Task.FromResult(Fail("Mini App authorization is not configured on the server."));
        }

        var parsedData = QueryHelpers.ParseQuery(initData);
        if (!parsedData.TryGetValue(HashKey, out var hashValues))
        {
            return Task.FromResult(Fail("Telegram authorization payload is incomplete."));
        }

        var receivedHash = hashValues.ToString();
        if (string.IsNullOrWhiteSpace(receivedHash))
        {
            return Task.FromResult(Fail("Telegram authorization hash is missing."));
        }

        var dataCheckString = BuildDataCheckString(parsedData);
        var computedHash = ComputeHash(botToken, dataCheckString);

        if (!FixedTimeEquals(receivedHash, computedHash))
        {
            return Task.FromResult(Fail("Telegram authorization could not be verified."));
        }

        if (!TryReadAuthDate(parsedData, out var authDateUtc))
        {
            return Task.FromResult(Fail("Telegram authorization date is missing."));
        }

        var lifetime = TimeSpan.FromSeconds(Math.Max(60, miniAppOptions.Value.InitDataLifetimeSeconds));
        if (DateTimeOffset.UtcNow - authDateUtc > lifetime)
        {
            return Task.FromResult(Fail("Telegram authorization expired. Reopen the Mini App from the bot."));
        }

        if (!parsedData.TryGetValue("user", out var userValues))
        {
            return Task.FromResult(Fail("Telegram user payload is missing."));
        }

        TelegramMiniAppUserPayload? user;
        try
        {
            user = JsonSerializer.Deserialize<TelegramMiniAppUserPayload>(userValues.ToString(), JsonOptions);
        }
        catch (JsonException)
        {
            return Task.FromResult(Fail("Telegram user payload is invalid."));
        }

        if (user?.Id is null or 0)
        {
            return Task.FromResult(Fail("Telegram user is missing from the Mini App session."));
        }

        return Task.FromResult(new MiniAppAuthResultDto(
            true,
            user.Id,
            user.Username,
            user.FirstName,
            user.LastName,
            null));
    }

    private static bool TryReadAuthDate(
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> parsedData,
        out DateTimeOffset authDateUtc)
    {
        authDateUtc = default;
        if (!parsedData.TryGetValue("auth_date", out var authDateValues))
        {
            return false;
        }

        if (!long.TryParse(authDateValues.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimeSeconds))
        {
            return false;
        }

        authDateUtc = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
        return true;
    }

    private static string BuildDataCheckString(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> parsedData) =>
        string.Join(
            "\n",
            parsedData
                .Where(pair => !string.Equals(pair.Key, HashKey, StringComparison.Ordinal) &&
                               !string.Equals(pair.Key, SignatureKey, StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value.ToString()}"));

    private static string ComputeHash(string botToken, string dataCheckString)
    {
        var secretKey = ComputeHmacSha256(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        var hashBytes = ComputeHmacSha256(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static byte[] ComputeHmacSha256(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    private static bool FixedTimeEquals(string leftHex, string rightHex)
    {
        try
        {
            var leftBytes = Convert.FromHexString(leftHex);
            var rightBytes = Convert.FromHexString(rightHex);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static MiniAppAuthResultDto Fail(string message) =>
        new(false, null, null, null, null, message);

    private sealed class TelegramMiniAppUserPayload
    {
        public long? Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
