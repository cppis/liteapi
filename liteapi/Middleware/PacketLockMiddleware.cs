using liteapi.Models;
using liteapi.Services;

namespace liteapi.Middleware;

/// <summary>
/// Middleware to automatically acquire and release DB locks for authenticated requests
/// </summary>
public class PacketLockMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PacketLockMiddleware> _logger;

    public PacketLockMiddleware(RequestDelegate next, ILogger<PacketLockMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestContext requestContext, DbLockService lockService)
    {
        // Skip lock for unauthenticated requests or health checks
        if (!requestContext.IsAuthenticated || context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var userId = requestContext.UserId;
        var lockAcquired = false;

        try
        {
            // Acquire lock
            lockAcquired = await lockService.AcquireLockAsync(userId);

            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for user {UserId} on path {Path}", userId, context.Request.Path);
                context.Response.StatusCode = 409; // Conflict
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "LOCK_ACQUISITION_FAILED",
                    message = "Could not acquire user lock. Please try again."
                });
                return;
            }

            _logger.LogDebug("Lock acquired for user {UserId} on path {Path}", userId, context.Request.Path);

            // Execute the request
            await _next(context);
        }
        finally
        {
            // Release lock if it was acquired
            if (lockAcquired)
            {
                await lockService.ReleaseLockAsync(userId);
                _logger.LogDebug("Lock released for user {UserId}", userId);
            }
        }
    }
}

/// <summary>
/// Extension method to add PacketLockMiddleware to the pipeline
/// </summary>
public static class PacketLockMiddlewareExtensions
{
    public static IApplicationBuilder UsePacketLock(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PacketLockMiddleware>();
    }
}
