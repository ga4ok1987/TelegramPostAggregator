using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

public sealed class FactCheckDispatchJob(IFactCheckService factCheckService)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        factCheckService.ProcessPendingRequestsAsync(cancellationToken);
}
