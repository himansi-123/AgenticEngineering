using AgenticEngineering.Domain;

namespace AgenticEngineering.Application.ShortUrls;

public sealed record ShortUrlDto(
    string Code,
    string OriginalUrl,
    DateTimeOffset CreatedAtUtc,
    long ClickCount,
    DateTimeOffset? LastClickedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsExpired);

public sealed record UrlAnalyticsDto(
    string Code,
    long TotalClicks,
    IReadOnlyList<DateTimeOffset> ClickTimestampsUtc);

public interface IShortUrlRepository
{
    Task<bool> TryCreateAsync(ShortUrl shortUrl, CancellationToken cancellationToken);

    Task<ShortUrl?> GetAsync(string code, CancellationToken cancellationToken);

    Task<ShortUrl?> RecordClickAsync(string code, DateTimeOffset clickedAtUtc, CancellationToken cancellationToken);
}

public interface IShortCodeGenerator
{
    string Generate();
}

public interface IShortUrlService
{
    Task<ShortUrlDto> CreateAsync(string? originalUrl, CancellationToken cancellationToken);

    Task<ShortUrlDto> CreateAsync(string? originalUrl, DateTimeOffset? expiresAtUtc, CancellationToken cancellationToken);

    Task<ShortUrlDto?> GetAsync(string code, CancellationToken cancellationToken);

    Task<ShortUrlDto?> RecordClickAsync(string code, CancellationToken cancellationToken);

    Task<UrlAnalyticsDto?> GetAnalyticsAsync(string code, CancellationToken cancellationToken);
}

public sealed class InvalidDestinationException(string message) : Exception(message);

public sealed class ShortCodeGenerationException()
    : Exception("A unique short code could not be generated after several attempts.");