namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IErrorAlertService
{
    Task SendAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default);
}
