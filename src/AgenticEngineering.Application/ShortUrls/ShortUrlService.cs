using System.Security.Cryptography;
using AgenticEngineering.Domain;

namespace AgenticEngineering.Application.ShortUrls;

public sealed class ShortUrlService(
    IShortUrlRepository repository,
    IShortCodeGenerator codeGenerator) : IShortUrlService
{
    private const int MaximumCreateAttempts = 5;
    private const int MaximumUrlLength = 2048;

    public async Task<ShortUrlDto> CreateAsync(string? originalUrl, CancellationToken cancellationToken)
    {
        return await CreateAsync(originalUrl, null, cancellationToken);
    }

    public async Task<ShortUrlDto> CreateAsync(
        string? originalUrl,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (!IsSafeHttpUrl(originalUrl))
        {
            throw new InvalidDestinationException("URL must be an absolute HTTP or HTTPS URL no longer than 2048 characters and must not contain credentials.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new InvalidDestinationException("Expiration must be later than the current time.");
        }

        for (var attempt = 0; attempt < MaximumCreateAttempts; attempt++)
        {
            var shortUrl = new ShortUrl(codeGenerator.Generate(), originalUrl!, createdAtUtc, expiresAtUtc);
            if (await repository.TryCreateAsync(shortUrl, cancellationToken))
            {
                return ToDto(shortUrl, createdAtUtc);
            }
        }

        throw new ShortCodeGenerationException();
    }

    public async Task<ShortUrlDto?> GetAsync(string code, CancellationToken cancellationToken)
    {
        var shortUrl = await repository.GetAsync(code, cancellationToken);
        return shortUrl is null ? null : ToDto(shortUrl, DateTimeOffset.UtcNow);
    }

    public async Task<ShortUrlDto?> RecordClickAsync(string code, CancellationToken cancellationToken)
    {
        var shortUrl = await repository.RecordClickAsync(code, DateTimeOffset.UtcNow, cancellationToken);
        return shortUrl is null ? null : ToDto(shortUrl, DateTimeOffset.UtcNow);
    }

    public async Task<UrlAnalyticsDto?> GetAnalyticsAsync(string code, CancellationToken cancellationToken)
    {
        var shortUrl = await repository.GetAsync(code, cancellationToken);
        if (shortUrl is null)
        {
            return null;
        }

        var timestamps = shortUrl.Clicks
            .Select(click => click.ClickedAtUtc)
            .OrderBy(timestamp => timestamp)
            .ToArray();

        return new UrlAnalyticsDto(shortUrl.Code, timestamps.LongLength, timestamps);
    }

    private static bool IsSafeHttpUrl(string? value)
    {
        return value is { Length: > 0 and <= MaximumUrlLength }
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static ShortUrlDto ToDto(ShortUrl shortUrl, DateTimeOffset nowUtc)
    {
        DateTimeOffset? lastClickedAtUtc = shortUrl.Clicks.Count == 0
            ? null
            : shortUrl.Clicks.Max(click => click.ClickedAtUtc);

        return new ShortUrlDto(
            shortUrl.Code,
            shortUrl.OriginalUrl,
            shortUrl.CreatedAtUtc,
            shortUrl.Clicks.Count,
            lastClickedAtUtc,
            shortUrl.ExpiresAtUtc,
            shortUrl.IsExpired(nowUtc));
    }
}

public sealed class RandomShortCodeGenerator : IShortCodeGenerator
{
    public string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
}