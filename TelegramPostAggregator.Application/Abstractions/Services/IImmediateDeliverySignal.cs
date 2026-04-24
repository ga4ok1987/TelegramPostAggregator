namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IImmediateDeliverySignal
{
    void Signal();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
