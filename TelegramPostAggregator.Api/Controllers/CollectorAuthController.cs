using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/collector-auth")]
public sealed class CollectorAuthController(ICollectorAuthService collectorAuthService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(CollectorAuthStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectorAuthStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await collectorAuthService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpPost("start")]
    [ProducesResponseType(typeof(CollectorAuthStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectorAuthStatusDto>> Start(CancellationToken cancellationToken)
    {
        var status = await collectorAuthService.StartAsync(cancellationToken);
        return Ok(status);
    }

    [HttpPost("code")]
    [ProducesResponseType(typeof(CollectorAuthStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectorAuthStatusDto>> SubmitCode([FromBody] SubmitCollectorCodeDto request, CancellationToken cancellationToken)
    {
        var status = await collectorAuthService.SubmitCodeAsync(request, cancellationToken);
        return Ok(status);
    }

    [HttpPost("password")]
    [ProducesResponseType(typeof(CollectorAuthStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectorAuthStatusDto>> SubmitPassword([FromBody] SubmitCollectorPasswordDto request, CancellationToken cancellationToken)
    {
        var status = await collectorAuthService.SubmitPasswordAsync(request, cancellationToken);
        return Ok(status);
    }
}
