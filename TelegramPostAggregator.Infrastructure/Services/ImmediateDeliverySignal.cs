using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class ImmediateDeliverySignal : IImmediateDeliverySignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pending signal is already enough to wake the delivery loop.
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            await _signal.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }
}
