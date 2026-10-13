using AgenticEngineering.Application.ShortUrls;
using Microsoft.AspNetCore.Mvc;

namespace AgenticEngineering.Api.Controllers;

[ApiController]
[Route("api/urls")]
public sealed class UrlsController(IShortUrlService shortUrlService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ShortUrlDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShortUrlDto>> Create(
        [FromBody] CreateShortUrlRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await shortUrlService.CreateAsync(request.Url, cancellationToken);
            return CreatedAtAction(nameof(Get), new { code = result.Code }, result);
        }
        catch (InvalidDestinationException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid URL",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpGet("{code}")]
    [ProducesResponseType<ShortUrlDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShortUrlDto>> Get(string code, CancellationToken cancellationToken)
    {
        var result = await shortUrlService.GetAsync(code, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{code}/analytics")]
    [ProducesResponseType<UrlAnalyticsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UrlAnalyticsDto>> GetAnalytics(string code, CancellationToken cancellationToken)
    {
        var result = await shortUrlService.GetAnalyticsAsync(code, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}

public sealed class CreateShortUrlRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(2048)]
    public string? Url { get; init; }
}