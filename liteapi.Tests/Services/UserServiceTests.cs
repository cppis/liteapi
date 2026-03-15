using FluentAssertions;
using liteapi.Data;
using liteapi.Models;
using liteapi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace liteapi.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CacheService _cacheService;
    private readonly UserService _userService;
    private readonly IDistributedCache _cache;

    public UserServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new AppDbContext(dbOptions);

        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultTTLSeconds"] = "300",
                ["Redis:ListTTLSeconds"] = "30"
            })
            .Build();

        _cacheService = new CacheService(
            _cache,
            new MetricsService(),
            config,
            Mock.Of<ILogger<CacheService>>());

        _userService = new UserService(_db, _cacheService, config);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<User> SeedUserAsync(ulong id = 1, string username = "testuser")
    {
        var user = new User
        {
            UserId = id,
            Username = username,
            Email = $"{username}@test.com",
            Level = 1,
            Gold = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return user;
    }

    // ══════════════════════════════════════
    //  GetByIdAsync (Read-only Load)
    // ══════════════════════════════════════

    [Fact]
    public async Task GetByIdAsync_UserExists_ReturnsUser()
    {
        await SeedUserAsync();

        var result = await _userService.GetByIdAsync(1);

        result.Should().NotBeNull();
        result!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetByIdAsync_UserNotFound_ReturnsNull()
    {
        var result = await _userService.GetByIdAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SecondCall_ReturnsCachedValue()
    {
        await SeedUserAsync();

        // First call — cache miss, DB hit
        var first = await _userService.GetByIdAsync(1);

        // Modify DB directly (simulating another process)
        var dbUser = await _db.Users.FindAsync((ulong)1);
        dbUser!.Username = "modified";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Second call — should return cached (old) value
        var second = await _userService.GetByIdAsync(1);

        second!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetByIdAsync_NotTrackedByChangeTracker()
    {
        await SeedUserAsync();

        await _userService.GetByIdAsync(1);

        // AsNoTracking: entity should not be in ChangeTracker
        _db.ChangeTracker.Entries().Should().BeEmpty();
    }

    // ══════════════════════════════════════
    //  GetAllAsync (Read-only Load)
    // ══════════════════════════════════════

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsers()
    {
        await SeedUserAsync(1, "user1");
        await SeedUserAsync(2, "user2");

        var result = await _userService.GetAllAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_EmptyDb_ReturnsEmptyList()
    {
        var result = await _userService.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_SecondCall_ReturnsCachedList()
    {
        await SeedUserAsync(1, "user1");

        // First call — cache miss
        var first = await _userService.GetAllAsync();

        // Add another user directly to DB
        await SeedUserAsync(2, "user2");

        // Second call — cached, still 1 user
        var second = await _userService.GetAllAsync();

        second.Should().HaveCount(1);
    }

    // ══════════════════════════════════════
    //  LoadAsync (Write Load)
    // ══════════════════════════════════════

    [Fact]
    public async Task LoadAsync_UserExists_ReturnsTrackedEntity()
    {
        await SeedUserAsync();

        var result = await _userService.LoadAsync(1);

        result.Should().NotBeNull();
        _db.ChangeTracker.Entries().Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAsync_UserNotFound_ReturnsNull()
    {
        var result = await _userService.LoadAsync(999);

        result.Should().BeNull();
    }

    // ══════════════════════════════════════
    //  SaveAsync (Write Save)
    // ══════════════════════════════════════

    [Fact]
    public async Task SaveAsync_PersistsChangesToDb()
    {
        await SeedUserAsync();
        var user = await _userService.LoadAsync(1);
        user!.Gold = 999;

        await _userService.SaveAsync(user);

        _db.ChangeTracker.Clear();
        var saved = await _db.Users.FindAsync((ulong)1);
        saved!.Gold.Should().Be(999);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt()
    {
        await SeedUserAsync();
        var user = await _userService.LoadAsync(1);
        var oldTime = user!.UpdatedAt;

        await Task.Delay(10);
        await _userService.SaveAsync(user);

        user.UpdatedAt.Should().BeAfter(oldTime);
    }

    [Fact]
    public async Task SaveAsync_InvalidatesCachedUser()
    {
        await SeedUserAsync();

        // Populate cache
        await _userService.GetByIdAsync(1);

        // Load, modify, save
        var user = await _userService.LoadAsync(1);
        user!.Username = "updated";
        await _userService.SaveAsync(user);
        _db.ChangeTracker.Clear();

        // Next GetByIdAsync should hit DB (cache was invalidated)
        var fresh = await _userService.GetByIdAsync(1);
        fresh!.Username.Should().Be("updated");
    }

    // ══════════════════════════════════════
    //  CreateAsync
    // ══════════════════════════════════════

    [Fact]
    public async Task CreateAsync_SavesUserToDb()
    {
        var user = new User { UserId = 10, Username = "newuser", Email = "new@test.com" };

        var result = await _userService.CreateAsync(user);

        result.UserId.Should().Be(10);
        var saved = await _db.Users.FindAsync((ulong)10);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_SetsTimestamps()
    {
        var user = new User { UserId = 10, Username = "newuser" };

        var result = await _userService.CreateAsync(user);

        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    // ══════════════════════════════════════
    //  DeleteAsync
    // ══════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_UserExists_DeletesAndReturnsTrue()
    {
        await SeedUserAsync();

        var result = await _userService.DeleteAsync(1);

        result.Should().BeTrue();
        var deleted = await _db.Users.FindAsync((ulong)1);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UserNotFound_ReturnsFalse()
    {
        var result = await _userService.DeleteAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesCachedUser()
    {
        await SeedUserAsync();

        // Populate cache
        await _userService.GetByIdAsync(1);

        // Delete
        await _userService.DeleteAsync(1);

        // Cache should be invalidated — next call returns null (user deleted)
        var result = await _userService.GetByIdAsync(1);
        result.Should().BeNull();
    }

    // ══════════════════════════════════════
    //  Load → Process → Save pattern
    // ══════════════════════════════════════

    [Fact]
    public async Task FullPattern_Load_Process_Save()
    {
        await SeedUserAsync();

        // 1. Load
        var user = await _userService.LoadAsync(1);
        user.Should().NotBeNull();

        // 2. Process (in-memory only)
        user!.Gold += 500;
        user.Level = 10;

        // 3. Save
        await _userService.SaveAsync(user);

        // Verify
        _db.ChangeTracker.Clear();
        var saved = await _db.Users.FindAsync((ulong)1);
        saved!.Gold.Should().Be(600); // 100 + 500
        saved.Level.Should().Be(10);
    }

    [Fact]
    public async Task FullPattern_CacheInvalidatedAfterSave()
    {
        await SeedUserAsync();

        // Populate cache with old data
        var cached = await _userService.GetByIdAsync(1);
        cached!.Gold.Should().Be(100);

        // Load → Process → Save
        var user = await _userService.LoadAsync(1);
        user!.Gold = 9999;
        await _userService.SaveAsync(user);
        _db.ChangeTracker.Clear();

        // GetByIdAsync should return fresh data (cache was invalidated)
        var fresh = await _userService.GetByIdAsync(1);
        fresh!.Gold.Should().Be(9999);
    }
}
