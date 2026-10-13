namespace AgenticEngineering.Domain;

public sealed class ShortUrl
{
    private ShortUrl()
    {
    }

    public ShortUrl(string code, string originalUrl, DateTimeOffset createdAtUtc)
    {
        Code = code;
        OriginalUrl = originalUrl;
        CreatedAtUtc = createdAtUtc;
    }

    public string Code { get; private set; } = string.Empty;

    public string OriginalUrl { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ICollection<UrlClick> Clicks { get; private set; } = new List<UrlClick>();

    public void RecordClick(DateTimeOffset clickedAtUtc)
    {
        Clicks.Add(new UrlClick(Code, clickedAtUtc));
    }
}