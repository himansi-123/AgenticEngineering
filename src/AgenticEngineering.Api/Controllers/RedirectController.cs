using AgenticEngineering.Application.ShortUrls;
using Microsoft.AspNetCore.Mvc;

namespace AgenticEngineering.Api.Controllers;

[ApiController]
public sealed class RedirectController(IShortUrlService shortUrlService) : ControllerBase
{
    [HttpGet("/{code}")]
    public async Task<IActionResult> RedirectShortUrl(string code, CancellationToken cancellationToken)
    {
        var shortUrl = await shortUrlService.RecordClickAsync(code, cancellationToken);
        return shortUrl is null
            ? NotFound()
            : shortUrl.IsExpired
                ? StatusCode(StatusCodes.Status410Gone)
                : Redirect(shortUrl.OriginalUrl);
    }
}