using Microsoft.EntityFrameworkCore;
using liteapi.Data;
using liteapi.Models;

namespace liteapi.Services;

public class UserService
{
    private readonly AppDbContext _db;
    private readonly CacheService _cache;

    private readonly TimeSpan _userTTL;
    private readonly TimeSpan _listTTL;

    public UserService(
        AppDbContext db,
        CacheService cache,
        IConfiguration configuration)
    {
        _db = db;
        _cache = cache;
        _userTTL = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Redis:DefaultTTLSeconds", 300));
        _listTTL = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Redis:ListTTLSeconds", 30));
    }

    // -- Cache keys --
    private static string UserKey(ulong id) => $"user:{id}";
    private const string AllUsersKey = "users:all";

    // ======================================================
    //  Load -- fetch data before endpoint logic
    // ======================================================

    /// <summary>
    /// [Read-only Load] Cache -> DB fallback.
    /// AsNoTracking for performance (read-only, no ChangeTracker overhead).
    /// Use only in read-only endpoints.
    /// </summary>
    public async Task<User?> GetByIdAsync(ulong userId)
    {
        var cached = await _cache.GetAsync<User>(UserKey(userId));
        if (cached is not null)
            return cached;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user is not null)
            await _cache.SetAsync(UserKey(userId), user, _userTTL);

        return user;
    }

    /// <summary>
    /// [Read-only Load] List query. Cache -> DB fallback.
    /// AsNoTracking for performance. TTL 30s, no invalidation.
    /// </summary>
    public async Task<List<User>> GetAllAsync()
    {
        var cached = await _cache.GetAsync<List<User>>(AllUsersKey);
        if (cached is not null)
            return cached;

        var users = await _db.Users.AsNoTracking().ToListAsync();
        await _cache.SetAsync(AllUsersKey, users, _listTTL);
        return users;
    }

    /// <summary>
    /// [Write Load] Direct DB query. Tracked by ChangeTracker.
    /// Caller modifies properties then calls SaveAsync().
    /// Use only in write endpoints.
    /// </summary>
    public async Task<User?> LoadAsync(ulong userId)
    {
        return await _db.Users.FindAsync(userId);
    }

    // ======================================================
    //  Save -- persist changes after endpoint logic
    //  All write methods use explicit transactions.
    //  Cache invalidation happens AFTER transaction commit.
    // ======================================================

    /// <summary>
    /// [Save] DB save within transaction + invalidate user cache after commit.
    /// List cache is NOT invalidated (relies on TTL expiry).
    /// </summary>
    public async Task SaveAsync(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        await _cache.RemoveAsync(UserKey(user.UserId));
    }

    /// <summary>
    /// [Save] Create new user within transaction.
    /// List cache is NOT invalidated (relies on TTL expiry).
    /// </summary>
    public async Task<User> CreateAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return user;
    }

    /// <summary>
    /// [Load + Save] Delete within transaction + invalidate cache after commit.
    /// </summary>
    public async Task<bool> DeleteAsync(ulong userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        await _cache.RemoveAsync(UserKey(userId));
        return true;
    }
}
