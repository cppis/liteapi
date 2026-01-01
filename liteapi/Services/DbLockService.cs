using Microsoft.EntityFrameworkCore;
using liteapi.Data;

namespace liteapi.Services;

/// <summary>
/// Service for managing MySQL-based distributed locks using EF Core
/// Uses MySQL's GET_LOCK and RELEASE_LOCK functions
/// </summary>
public class DbLockService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _lockTimeoutSeconds;
    private readonly string _lockPrefix;
    private readonly ILogger<DbLockService> _logger;

    public DbLockService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DbLockService> logger)
    {
        _serviceProvider = serviceProvider;
        _lockTimeoutSeconds = configuration.GetValue<int>("Lock:TimeoutSeconds", 30);
        _lockPrefix = configuration.GetValue<string>("Lock:Prefix") ?? "api";
        _logger = logger;
    }

    /// <summary>
    /// Acquire a database lock for the given user ID
    /// </summary>
    /// <param name="userId">User ID to lock</param>
    /// <param name="dbContext">Optional existing DbContext (will create new scope if null)</param>
    /// <returns>True if lock was acquired, false otherwise</returns>
    public async Task<bool> AcquireLockAsync(ulong userId, AppDbContext? dbContext = null)
    {
        var lockName = GetLockName(userId);

        if (string.IsNullOrEmpty(lockName))
        {
            _logger.LogWarning("Lock name is empty for userId: {UserId}", userId);
            return false;
        }

        var shouldDisposeContext = false;
        if (dbContext == null)
        {
            var scope = _serviceProvider.CreateScope();
            dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            shouldDisposeContext = true;
        }

        try
        {
            var sql = $"SELECT GET_LOCK({{0}}, {{1}}) AS Result";
            var result = await dbContext.Database
                .SqlQuery<LockResult>($"SELECT GET_LOCK({lockName}, {_lockTimeoutSeconds}) AS Result")
                .FirstOrDefaultAsync();

            var lockResult = result?.Result ?? 0;

            if (lockResult <= 0)
            {
                _logger.LogWarning("Failed to acquire lock for userId: {UserId}, lockName: {LockName}", userId, lockName);
                return false;
            }

            _logger.LogDebug("Lock acquired for userId: {UserId}, lockName: {LockName}", userId, lockName);
            return true;
        }
        finally
        {
            if (shouldDisposeContext && dbContext != null)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Release a database lock for the given user ID
    /// </summary>
    /// <param name="userId">User ID to unlock</param>
    /// <param name="dbContext">Optional existing DbContext (will create new scope if null)</param>
    /// <returns>True if lock was released, false otherwise</returns>
    public async Task<bool> ReleaseLockAsync(ulong userId, AppDbContext? dbContext = null)
    {
        var lockName = GetLockName(userId);

        var shouldDisposeContext = false;
        if (dbContext == null)
        {
            var scope = _serviceProvider.CreateScope();
            dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            shouldDisposeContext = true;
        }

        try
        {
            var result = await dbContext.Database
                .SqlQuery<UnlockResult>($"SELECT RELEASE_LOCK({lockName}) AS Result")
                .FirstOrDefaultAsync();

            var unlockResult = result?.Result ?? 0;

            if (unlockResult <= 0)
            {
                _logger.LogWarning("Failed to release lock for userId: {UserId}, lockName: {LockName}", userId, lockName);
                return false;
            }

            _logger.LogDebug("Lock released for userId: {UserId}, lockName: {LockName}", userId, lockName);
            return true;
        }
        finally
        {
            if (shouldDisposeContext && dbContext != null)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Execute an action with a database lock
    /// </summary>
    /// <param name="userId">User ID to lock</param>
    /// <param name="action">Action to execute while holding the lock</param>
    /// <returns>True if lock was acquired and action was executed, false otherwise</returns>
    public async Task<bool> ExecuteWithLockAsync(ulong userId, Func<Task> action)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lockAcquired = await AcquireLockAsync(userId, dbContext);
        if (!lockAcquired)
        {
            return false;
        }

        try
        {
            await action();
            return true;
        }
        finally
        {
            await ReleaseLockAsync(userId, dbContext);
        }
    }

    /// <summary>
    /// Execute a function with a database lock and return result
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="userId">User ID to lock</param>
    /// <param name="func">Function to execute while holding the lock</param>
    /// <returns>Result of the function, or default if lock could not be acquired</returns>
    public async Task<T?> ExecuteWithLockAsync<T>(ulong userId, Func<Task<T>> func)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lockAcquired = await AcquireLockAsync(userId, dbContext);
        if (!lockAcquired)
        {
            return default;
        }

        try
        {
            return await func();
        }
        finally
        {
            await ReleaseLockAsync(userId, dbContext);
        }
    }

    private string GetLockName(ulong userId)
    {
        return userId > 0 ? $"lock_{_lockPrefix}_{userId}" : string.Empty;
    }
}

/// <summary>
/// Helper class for GET_LOCK result
/// </summary>
public class LockResult
{
    public int Result { get; set; }
}

/// <summary>
/// Helper class for RELEASE_LOCK result
/// </summary>
public class UnlockResult
{
    public int Result { get; set; }
}
