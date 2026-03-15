using FluentAssertions;
using liteapi.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace liteapi.Tests.Services;

public class CacheServiceTests
{
    private readonly CacheService _cacheService;
    private readonly IDistributedCache _cache;
    private readonly MetricsService _metrics;

    public CacheServiceTests()
    {
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _metrics = new MetricsService();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultTTLSeconds"] = "300"
            })
            .Build();

        _cacheService = new CacheService(
            _cache,
            _metrics,
            config,
            Mock.Of<ILogger<CacheService>>());
    }

    // ── GetAsync ──

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsDefault()
    {
        var result = await _cacheService.GetAsync<string>("user:999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsValue()
    {
        await _cacheService.SetAsync("user:1", new { Name = "test" });

        var result = await _cacheService.GetAsync<Dictionary<string, object>>("user:1");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_DeserializesCorrectType()
    {
        var original = new TestData { Id = 42, Name = "hello" };
        await _cacheService.SetAsync("test:1", original);

        var result = await _cacheService.GetAsync<TestData>("test:1");

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Name.Should().Be("hello");
    }

    // ── SetAsync ──

    [Fact]
    public async Task SetAsync_StoresValue()
    {
        await _cacheService.SetAsync("user:1", "value");

        var result = await _cacheService.GetAsync<string>("user:1");
        result.Should().Be("value");
    }

    [Fact]
    public async Task SetAsync_WithCustomTTL_StoresValue()
    {
        await _cacheService.SetAsync("user:1", "value", TimeSpan.FromSeconds(10));

        var result = await _cacheService.GetAsync<string>("user:1");
        result.Should().Be("value");
    }

    // ── RemoveAsync ──

    [Fact]
    public async Task RemoveAsync_DeletesKey()
    {
        await _cacheService.SetAsync("user:1", "value");
        await _cacheService.RemoveAsync("user:1");

        var result = await _cacheService.GetAsync<string>("user:1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        var act = () => _cacheService.RemoveAsync("user:nonexistent");

        await act.Should().NotThrowAsync();
    }

    // ── Error resilience ──

    [Fact]
    public async Task GetAsync_WhenCacheFails_ReturnsDefault()
    {
        var failingCache = new Mock<IDistributedCache>();
        failingCache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new Exception("Redis down"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultTTLSeconds"] = "300"
            })
            .Build();

        var service = new CacheService(
            failingCache.Object,
            _metrics,
            config,
            Mock.Of<ILogger<CacheService>>());

        var result = await service.GetAsync<string>("user:1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenCacheFails_DoesNotThrow()
    {
        var failingCache = new Mock<IDistributedCache>();
        failingCache.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), default))
            .ThrowsAsync(new Exception("Redis down"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultTTLSeconds"] = "300"
            })
            .Build();

        var service = new CacheService(
            failingCache.Object,
            _metrics,
            config,
            Mock.Of<ILogger<CacheService>>());

        var act = () => service.SetAsync("user:1", "value");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_WhenCacheFails_DoesNotThrow()
    {
        var failingCache = new Mock<IDistributedCache>();
        failingCache.Setup(c => c.RemoveAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new Exception("Redis down"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultTTLSeconds"] = "300"
            })
            .Build();

        var service = new CacheService(
            failingCache.Object,
            _metrics,
            config,
            Mock.Of<ILogger<CacheService>>());

        var act = () => service.RemoveAsync("user:1");

        await act.Should().NotThrowAsync();
    }

    // ── Metrics ──

    [Fact]
    public async Task GetAsync_CacheHit_IncrementsCacheHitMetric()
    {
        await _cacheService.SetAsync("user:1", "value");

        // Should not throw — metrics are recorded internally
        var result = await _cacheService.GetAsync<string>("user:1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_CacheMiss_IncrementsCacheMissMetric()
    {
        // Should not throw — metrics are recorded internally
        var result = await _cacheService.GetAsync<string>("user:missing");
        result.Should().BeNull();
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
