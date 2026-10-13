namespace AgenticEngineering.Domain;

public sealed class UrlClick
{
    private UrlClick()
    {
    }

    public UrlClick(string shortUrlCode, DateTimeOffset clickedAtUtc)
    {
        ShortUrlCode = shortUrlCode;
        ClickedAtUtc = clickedAtUtc;
    }

    public long Id { get; private set; }

    public string ShortUrlCode { get; private set; } = string.Empty;

    public DateTimeOffset ClickedAtUtc { get; private set; }
}