using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TdLib;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TdLibCollectorClientManager : IAsyncDisposable
{
    private readonly TdLibOptions _options;
    private readonly ILogger<TdLibCollectorClientManager> _logger;
    private readonly ConcurrentDictionary<Guid, CollectorRuntime> _clients = new();

    public TdLibCollectorClientManager(IOptions<TdLibOptions> options, ILogger<TdLibCollectorClientManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CollectorAuthStatusDto> InitializeAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken)
    {
        try
        {
            var runtime = await GetOrCreateRuntimeAsync(collectorAccount, cancellationToken);
            return runtime.ToStatusDto(collectorAccount);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to initialize TDLib collector status for account {CollectorAccountKey}", collectorAccount.ExternalAccountKey);
            return new CollectorAuthStatusDto(
                collectorAccount.Id,
                collectorAccount.Name,
                collectorAccount.Status.ToString(),
                false,
                null,
                null,
                exception.Message,
                DateTimeOffset.UtcNow);
        }
    }

    public async Task<CollectorAuthStatusDto> GetStatusAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken)
    {
        try
        {
            var runtime = await GetOrCreateRuntimeAsync(collectorAccount, cancellationToken);
            return runtime.ToStatusDto(collectorAccount);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read TDLib collector status for account {CollectorAccountKey}", collectorAccount.ExternalAccountKey);
            return new CollectorAuthStatusDto(
                collectorAccount.Id,
                collectorAccount.Name,
                collectorAccount.Status.ToString(),
                false,
                null,
                null,
                exception.Message,
                DateTimeOffset.UtcNow);
        }
    }

    public async Task<CollectorAuthStatusDto> SubmitCodeAsync(CollectorAccount collectorAccount, string code, CancellationToken cancellationToken)
    {
        var runtime = await GetOrCreateRuntimeAsync(collectorAccount, cancellationToken);
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            await runtime.Client.ExecuteAsync(new TdApi.CheckAuthenticationCode { Code = code });
            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            runtime.LastError = null;
            return runtime.ToStatusDto(collectorAccount);
        }
        catch (Exception exception)
        {
            runtime.LastError = exception.Message;
            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return runtime.ToStatusDto(collectorAccount);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    public async Task<CollectorAuthStatusDto> SubmitPasswordAsync(CollectorAccount collectorAccount, string password, CancellationToken cancellationToken)
    {
        var runtime = await GetOrCreateRuntimeAsync(collectorAccount, cancellationToken);
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            await runtime.Client.ExecuteAsync(new TdApi.CheckAuthenticationPassword { Password = password });
            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            runtime.LastError = null;
            return runtime.ToStatusDto(collectorAccount);
        }
        catch (Exception exception)
        {
            runtime.LastError = exception.Message;
            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return runtime.ToStatusDto(collectorAccount);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    public async Task<TdClient> GetAuthorizedClientAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken)
    {
        var runtime = await GetOrCreateRuntimeAsync(collectorAccount, cancellationToken);
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(5, _options.AuthorizationTimeoutSeconds));

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (runtime.IsReady)
            {
                return runtime.Client;
            }

            if (!string.IsNullOrWhiteSpace(runtime.LastError))
            {
                throw new InvalidOperationException($"TDLib collector is not authorized: {runtime.LastError}");
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException($"TDLib collector isn't ready. Current state: {runtime.AuthorizationStateName ?? "unknown"}");
    }

    public async Task<string?> DownloadFileAndGetPathAsync(
        CollectorAccount collectorAccount,
        int fileId,
        CancellationToken cancellationToken)
    {
        var client = await GetAuthorizedClientAsync(collectorAccount, cancellationToken);
        var downloaded = await client.ExecuteAsync(new TdApi.DownloadFile
        {
            FileId = fileId,
            Priority = 16,
            Offset = 0,
            Limit = 0,
            Synchronous = true
        });

        return downloaded.Local.IsDownloadingCompleted && !string.IsNullOrWhiteSpace(downloaded.Local.Path)
            ? downloaded.Local.Path
            : null;
    }

    private async Task<CollectorRuntime> GetOrCreateRuntimeAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(collectorAccount.Id, out var existing))
        {
            await EnsureInitializedAsync(existing, collectorAccount, cancellationToken);
            return existing;
        }

        var created = new CollectorRuntime(new TdClient());
        var runtime = _clients.GetOrAdd(collectorAccount.Id, created);

        if (ReferenceEquals(runtime, created))
        {
            runtime.Client.UpdateReceived += async (_, update) => await HandleUpdateAsync(runtime, collectorAccount, update);
        }
        else
        {
            created.Client.Dispose();
            created.Gate.Dispose();
        }

        await EnsureInitializedAsync(runtime, collectorAccount, cancellationToken);
        return runtime;
    }

    private async Task EnsureInitializedAsync(CollectorRuntime runtime, CollectorAccount collectorAccount, CancellationToken cancellationToken)
    {
        if (runtime.IsInitialized)
        {
            return;
        }

        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            if (runtime.IsInitialized)
            {
                return;
            }

            if (!runtime.LogVerbosityConfigured)
            {
                await runtime.Client.ExecuteAsync(new TdApi.SetLogVerbosityLevel { NewVerbosityLevel = 1 });
                runtime.LogVerbosityConfigured = true;
            }

            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var state = await runtime.Client.ExecuteAsync(new TdApi.GetAuthorizationState());
            await HandleAuthorizationStateAsync(runtime, collectorAccount, state);
            runtime.IsInitialized = true;
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    private async Task HandleUpdateAsync(CollectorRuntime runtime, CollectorAccount collectorAccount, TdApi.Update update)
    {
        try
        {
            switch (update)
            {
                case TdApi.Update.UpdateAuthorizationState authUpdate:
                    await HandleAuthorizationStateAsync(runtime, collectorAccount, authUpdate.AuthorizationState);
                    break;
                case TdApi.Update.UpdateConnectionState:
                    runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    break;
            }
        }
        catch (Exception exception)
        {
            runtime.LastError = exception.Message;
            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            runtime.IsReady = false;
            _logger.LogWarning(exception, "TDLib collector auth flow failed for account {CollectorAccountKey}", collectorAccount.ExternalAccountKey);
        }
    }

    private async Task HandleAuthorizationStateAsync(CollectorRuntime runtime, CollectorAccount collectorAccount, TdApi.AuthorizationState state)
    {
        runtime.AuthorizationStateName = state.DataType;
        runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
        runtime.IsReady = state is TdApi.AuthorizationState.AuthorizationStateReady;

        switch (state)
        {
            case TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters:
                if (runtime.TdlibParametersSubmitted)
                {
                    break;
                }

                await runtime.Client.ExecuteAsync(new TdApi.SetTdlibParameters
                {
                    DatabaseDirectory = Path.Combine(_options.DatabaseDirectory, collectorAccount.ExternalAccountKey),
                    FilesDirectory = Path.Combine(_options.FilesDirectory, collectorAccount.ExternalAccountKey),
                    UseTestDc = false,
                    UseFileDatabase = true,
                    UseChatInfoDatabase = true,
                    UseMessageDatabase = true,
                    UseSecretChats = false,
                    ApiId = _options.ApiId,
                    ApiHash = _options.ApiHash,
                    DeviceModel = _options.DeviceModel,
                    SystemVersion = _options.SystemVersion,
                    SystemLanguageCode = _options.SystemLanguageCode,
                    ApplicationVersion = _options.ApplicationVersion,
                    DatabaseEncryptionKey = Array.Empty<byte>()
                });
                runtime.TdlibParametersSubmitted = true;
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber:
                if (runtime.PhoneNumberSubmitted)
                {
                    break;
                }

                var phoneNumber = NormalizePhoneNumber(
                    string.IsNullOrWhiteSpace(collectorAccount.PhoneNumber) ? _options.PhoneNumber : collectorAccount.PhoneNumber);

                await runtime.Client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
                {
                    PhoneNumber = phoneNumber,
                    Settings = new TdApi.PhoneNumberAuthenticationSettings
                    {
                        AllowFlashCall = false,
                        AllowMissedCall = false,
                        IsCurrentPhoneNumber = false,
                        HasUnknownPhoneNumber = false,
                        AllowSmsRetrieverApi = false
                    }
                });
                runtime.PhoneNumberSubmitted = true;
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitCode:
                runtime.LastError = "Collector account requires Telegram login code.";
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitPassword waitPassword:
                runtime.PasswordHint = waitPassword.PasswordHint;
                runtime.LastError = "Collector account requires 2FA password.";
                break;
            case TdApi.AuthorizationState.AuthorizationStateReady:
                runtime.LastError = null;
                runtime.PasswordHint = null;
                runtime.PhoneNumberSubmitted = false;
                break;
            case TdApi.AuthorizationState.AuthorizationStateLoggingOut:
            case TdApi.AuthorizationState.AuthorizationStateClosed:
                runtime.LastError = "TDLib session is closed. Restart authentication.";
                runtime.IsReady = false;
                runtime.IsInitialized = false;
                runtime.TdlibParametersSubmitted = false;
                runtime.PhoneNumberSubmitted = false;
                break;
        }
    }

    private static string NormalizePhoneNumber(string phoneNumber)
    {
        var normalized = Regex.Replace(phoneNumber, "[^0-9+]", string.Empty);

        if (normalized.StartsWith("00", StringComparison.Ordinal))
        {
            normalized = "+" + normalized[2..];
        }

        return normalized;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _clients.Values)
        {
            try
            {
                await runtime.Client.ExecuteAsync(new TdApi.Close());
            }
            catch
            {
                // Ignore shutdown errors.
            }

            runtime.Client.Dispose();
            runtime.Gate.Dispose();
        }
    }

    private sealed class CollectorRuntime
    {
        public CollectorRuntime(TdClient client)
        {
            Client = client;
        }

        public TdClient Client { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? AuthorizationStateName { get; set; }
        public string? PasswordHint { get; set; }
        public string? LastError { get; set; }
        public bool IsReady { get; set; }
        public bool IsInitialized { get; set; }
        public bool LogVerbosityConfigured { get; set; }
        public bool TdlibParametersSubmitted { get; set; }
        public bool PhoneNumberSubmitted { get; set; }
        public DateTimeOffset? UpdatedAtUtc { get; set; }

        public CollectorAuthStatusDto ToStatusDto(CollectorAccount collectorAccount) =>
            new(
                collectorAccount.Id,
                collectorAccount.Name,
                collectorAccount.Status.ToString(),
                IsReady,
                AuthorizationStateName,
                PasswordHint,
                LastError,
                UpdatedAtUtc);
    }
}
