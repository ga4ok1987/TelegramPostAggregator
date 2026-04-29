using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services.Monitoring;

public sealed class TelegramMiniAppAuthService(
    IAppUserRepository appUserRepository,
    IOptions<MiniAppOptions> miniAppOptions,
    IOptions<TelegramBotOptions> telegramBotOptions,
    ILogger<TelegramMiniAppAuthService> logger) : ITelegramMiniAppAuthService
{
    private const string HashKey = "hash";
    private const string SignatureKey = "signature";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MiniAppAuthResultDto> AuthenticateAsync(string? initData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(initData))
        {
            return Fail("Open this screen from the Telegram bot menu.");
        }

        var botToken = telegramBotOptions.Value.BotToken;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return Fail("Mini App authorization is not configured on the server.");
        }

        var parsedData = ParseInitData(initData);
        if (!TryGetValue(parsedData, HashKey, out var receivedHash))
        {
            return Fail("Telegram authorization payload is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(receivedHash))
        {
            return Fail("Telegram authorization hash is missing.");
        }

        var hashCandidates = BuildHashCandidates(botToken, parsedData);
        var matchedCandidate = hashCandidates.FirstOrDefault(candidate => FixedTimeEquals(receivedHash, candidate.Hash));

        if (matchedCandidate is null)
        {
            logger.LogWarning(
                "Mini App authorization hash mismatch. Keys: {Keys}. Hash prefix: {HashPrefix}. Signature present: {HasSignature}.",
                string.Join(", ", parsedData.Select(static item => item.Key).OrderBy(static value => value, StringComparer.Ordinal)),
                TruncateForLog(receivedHash, 10),
                parsedData.Any(static pair => string.Equals(pair.Key, SignatureKey, StringComparison.Ordinal)));
            return Fail("Telegram authorization could not be verified.");
        }

        logger.LogInformation(
            "Mini App authorization verified using {CandidateName}. Signature present: {HasSignature}.",
            matchedCandidate.Name,
            parsedData.Any(static pair => string.Equals(pair.Key, SignatureKey, StringComparison.Ordinal)));

        if (!TryReadAuthDate(parsedData, out var authDateUtc))
        {
            return Fail("Telegram authorization date is missing.");
        }

        var lifetime = TimeSpan.FromSeconds(Math.Max(60, miniAppOptions.Value.InitDataLifetimeSeconds));
        if (DateTimeOffset.UtcNow - authDateUtc > lifetime)
        {
            return Fail("Telegram authorization expired. Reopen the Mini App from the bot.");
        }

        if (!TryGetValue(parsedData, "user", out var userPayload))
        {
            return Fail("Telegram user payload is missing.");
        }

        TelegramMiniAppUserPayload? user;
        try
        {
            user = JsonSerializer.Deserialize<TelegramMiniAppUserPayload>(userPayload, JsonOptions);
        }
        catch (JsonException)
        {
            return Fail("Telegram user payload is invalid.");
        }

        if (user?.Id is null or 0)
        {
            return Fail("Telegram user is missing from the Mini App session.");
        }

        var appUser = await appUserRepository.GetByTelegramUserIdAsync(user.Id.Value, cancellationToken);
        if (appUser is null)
        {
            return Fail("Start the bot first, then open Mini App from the bot menu.");
        }

        if (appUser.IsBlockedBot)
        {
            return Fail("This Telegram account is not allowed to use the Mini App.");
        }

        return new MiniAppAuthResultDto(
            true,
            user.Id,
            user.Username,
            user.FirstName,
            user.LastName,
            null);
    }

    private static bool TryReadAuthDate(
        IReadOnlyList<InitDataItem> parsedData,
        out DateTimeOffset authDateUtc)
    {
        authDateUtc = default;
        if (!TryGetValue(parsedData, "auth_date", out var authDateValue))
        {
            return false;
        }

        if (!long.TryParse(authDateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimeSeconds))
        {
            return false;
        }

        authDateUtc = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
        return true;
    }

    private static List<HashCandidate> BuildHashCandidates(string botToken, IReadOnlyList<InitDataItem> parsedData)
    {
        var candidates = new List<HashCandidate>(8);
        AddCandidate(candidates, "decoded", botToken, BuildDataCheckString(parsedData, excludeSignature: false, static pair => pair.Value), useLegacy: false);
        AddCandidate(candidates, "decoded-no-signature", botToken, BuildDataCheckString(parsedData, excludeSignature: true, static pair => pair.Value), useLegacy: false);
        AddCandidate(candidates, "raw", botToken, BuildDataCheckString(parsedData, excludeSignature: false, static pair => pair.RawValue), useLegacy: false);
        AddCandidate(candidates, "raw-no-signature", botToken, BuildDataCheckString(parsedData, excludeSignature: true, static pair => pair.RawValue), useLegacy: false);
        AddCandidate(candidates, "decoded-legacy", botToken, BuildDataCheckString(parsedData, excludeSignature: false, static pair => pair.Value), useLegacy: true);
        AddCandidate(candidates, "decoded-no-signature-legacy", botToken, BuildDataCheckString(parsedData, excludeSignature: true, static pair => pair.Value), useLegacy: true);
        AddCandidate(candidates, "raw-legacy", botToken, BuildDataCheckString(parsedData, excludeSignature: false, static pair => pair.RawValue), useLegacy: true);
        AddCandidate(candidates, "raw-no-signature-legacy", botToken, BuildDataCheckString(parsedData, excludeSignature: true, static pair => pair.RawValue), useLegacy: true);
        return candidates;
    }

    private static void AddCandidate(List<HashCandidate> candidates, string name, string botToken, string dataCheckString, bool useLegacy)
    {
        var hash = useLegacy
            ? ComputeLegacyCompatibleHash(botToken, dataCheckString)
            : ComputeHash(botToken, dataCheckString);

        candidates.Add(new HashCandidate(name, hash));
    }

    private static string BuildDataCheckString(
        IReadOnlyList<InitDataItem> parsedData,
        bool excludeSignature,
        Func<InitDataItem, string> valueSelector) =>
        string.Join(
            "\n",
            parsedData
                .Where(pair => !string.Equals(pair.Key, HashKey, StringComparison.Ordinal) &&
                               (!excludeSignature || !string.Equals(pair.Key, SignatureKey, StringComparison.Ordinal)))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={valueSelector(pair)}"));

    private static List<InitDataItem> ParseInitData(string initData)
    {
        var items = new List<InitDataItem>();
        foreach (var segment in initData.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
            var rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;

            var key = DecodeFormComponent(rawKey);
            var value = DecodeFormComponent(rawValue);
            items.Add(new InitDataItem(key, value, rawValue));
        }

        return items;
    }

    private static string DecodeFormComponent(string value) =>
        Uri.UnescapeDataString(value.Replace("+", "%20", StringComparison.Ordinal));

    private static bool TryGetValue(IReadOnlyList<InitDataItem> items, string key, out string value)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Key, key, StringComparison.Ordinal))
            {
                value = item.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string ComputeHash(string botToken, string dataCheckString)
    {
        var secretKey = ComputeHmacSha256(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        var hashBytes = ComputeHmacSha256(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ComputeLegacyCompatibleHash(string botToken, string dataCheckString)
    {
        var secretKey = ComputeHmacSha256(
            Encoding.UTF8.GetBytes(botToken),
            Encoding.UTF8.GetBytes("WebAppData"));

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

    private static string TruncateForLog(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record HashCandidate(string Name, string Hash);

    private readonly record struct InitDataItem(string Key, string Value, string RawValue);

    private sealed class TelegramMiniAppUserPayload
    {
        public long? Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
