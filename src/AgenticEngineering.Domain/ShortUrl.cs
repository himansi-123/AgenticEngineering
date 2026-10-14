namespace AgenticEngineering.Domain;

public sealed class ShortUrl
{
    private ShortUrl()
    {
    }

    public ShortUrl(string code, string originalUrl, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc = null)
    {
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiration must be later than creation.");
        }

        Code = code;
        OriginalUrl = originalUrl;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string Code { get; private set; } = string.Empty;

    public string OriginalUrl { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public ICollection<UrlClick> Clicks { get; private set; } = new List<UrlClick>();

    public void RecordClick(DateTimeOffset clickedAtUtc)
    {
        if (IsExpired(clickedAtUtc))
        {
            throw new InvalidOperationException("An expired URL cannot record a click.");
        }

        Clicks.Add(new UrlClick(Code, clickedAtUtc));
    }

    public bool IsExpired(DateTimeOffset atUtc) => ExpiresAtUtc <= atUtc;
}