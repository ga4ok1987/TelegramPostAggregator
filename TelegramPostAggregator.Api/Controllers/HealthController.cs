using Microsoft.AspNetCore.Mvc;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new
        {
            status = "ok",
            utcNow = DateTimeOffset.UtcNow
        });

    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("error")]
    public IActionResult Error() =>
        Problem(title: "Unhandled server error", statusCode: StatusCodes.Status500InternalServerError);
}
