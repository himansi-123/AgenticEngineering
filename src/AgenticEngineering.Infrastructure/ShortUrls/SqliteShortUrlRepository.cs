using AgenticEngineering.Application.ShortUrls;
using AgenticEngineering.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticEngineering.Infrastructure.ShortUrls;

public sealed class SqliteShortUrlRepository(ShortUrlDbContext dbContext) : IShortUrlRepository
{
    public async Task<bool> TryCreateAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
    {
        dbContext.ShortUrls.Add(shortUrl);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(shortUrl).State = EntityState.Detached;
            return false;
        }
    }

    public Task<ShortUrl?> GetAsync(string code, CancellationToken cancellationToken)
    {
        return dbContext.ShortUrls
            .Include(shortUrl => shortUrl.Clicks)
            .SingleOrDefaultAsync(shortUrl => shortUrl.Code == code, cancellationToken);
    }

    public async Task<ShortUrl?> RecordClickAsync(
        string code,
        DateTimeOffset clickedAtUtc,
        CancellationToken cancellationToken)
    {
        var shortUrl = await dbContext.ShortUrls
            .Include(item => item.Clicks)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (shortUrl is null)
        {
            return null;
        }

        if (shortUrl.IsExpired(clickedAtUtc))
        {
            return shortUrl;
        }

        shortUrl.RecordClick(clickedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return shortUrl;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 1555 or 2067
        };
    }
}