using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(AggregatorDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new
        {
            status = "ok",
            utcNow = DateTimeOffset.UtcNow
        });

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var lastCollectorSyncAtUtc = await dbContext.ChannelCollectorAssignments
            .MaxAsync(x => (DateTimeOffset?)x.LastSyncedAtUtc, cancellationToken);

        var lastDeliveryAtUtc = await dbContext.UserChannelSubscriptions
            .MaxAsync(x => x.LastDeliveredAtUtc, cancellationToken);

        var activeSubscriptions = await dbContext.UserChannelSubscriptions
            .CountAsync(x => x.IsActive, cancellationToken);

        var pendingDeliverySubscriptions = await dbContext.UserChannelSubscriptions
            .Where(x => x.IsActive && x.LastDeliveredTelegramMessageId.HasValue)
            .CountAsync(
                x => dbContext.TelegramPosts.Any(post =>
                    post.ChannelId == x.ChannelId &&
                    post.TelegramMessageId > x.LastDeliveredTelegramMessageId!.Value),
                cancellationToken);

        return Ok(new
        {
            status = "ok",
            utcNow = DateTimeOffset.UtcNow,
            activeSubscriptions,
            pendingDeliverySubscriptions,
            lastCollectorSyncAtUtc,
            lastDeliveryAtUtc
        });
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("error")]
    public IActionResult Error() =>
        Problem(title: "Unhandled server error", statusCode: StatusCodes.Status500InternalServerError);
}
