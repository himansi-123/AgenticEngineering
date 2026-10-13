using AgenticEngineering.Application.ShortUrls;
using AgenticEngineering.Domain;

namespace AgenticEngineering.UnitTests;

public sealed class ShortUrlServiceTests
{
    [Fact]
    public async Task CreateAsync_rejects_non_http_urls()
    {
        var service = CreateService(new FakeRepository(), "code1");

        await Assert.ThrowsAsync<InvalidDestinationException>(
            () => service.CreateAsync("javascript:alert(1)", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://user:password@example.com")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public async Task CreateAsync_rejects_unsafe_or_non_absolute_urls(string url)
    {
        var service = CreateService(new FakeRepository(), "code1");

        await Assert.ThrowsAsync<InvalidDestinationException>(
            () => service.CreateAsync(url, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_retries_a_code_collision()
    {
        var repository = new FakeRepository { FailFirstCreate = true };
        var service = CreateService(repository, "duplicate", "unique");

        var result = await service.CreateAsync("https://example.com/path", CancellationToken.None);

        Assert.Equal("unique", result.Code);
        Assert.Equal(2, repository.CreateAttempts);
    }

    private static ShortUrlService CreateService(FakeRepository repository, params string[] codes)
    {
        return new ShortUrlService(repository, new SequenceCodeGenerator(codes));
    }

    private sealed class FakeRepository : IShortUrlRepository
    {
        public bool FailFirstCreate { get; init; }

        public int CreateAttempts { get; private set; }

        public Task<bool> TryCreateAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
        {
            CreateAttempts++;
            return Task.FromResult(!FailFirstCreate || CreateAttempts > 1);
        }

        public Task<ShortUrl?> GetAsync(string code, CancellationToken cancellationToken) => Task.FromResult<ShortUrl?>(null);

        public Task<ShortUrl?> RecordClickAsync(string code, DateTimeOffset clickedAtUtc, CancellationToken cancellationToken)
            => Task.FromResult<ShortUrl?>(null);
    }

    private sealed class SequenceCodeGenerator(params string[] codes) : IShortCodeGenerator
    {
        private int _index;

        public string Generate() => codes[Math.Min(_index++, codes.Length - 1)];
    }
}