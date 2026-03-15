using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using liteapi.Data;
using liteapi.Formatters;
using liteapi.Middleware;
using liteapi.Models;
using liteapi.Services;
using NetEscapades.Configuration.Yaml;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure YAML configuration
builder.Configuration
    .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
    .AddYamlFile($"appsettings.{builder.Environment.EnvironmentName}.yaml", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MVC Controllers with custom formatters
builder.Services.AddControllers(options =>
{
    // Add custom packet formatters (supports JSON and MessagePack)
    options.InputFormatters.Insert(0, new PacketInputFormatter());
    options.OutputFormatters.Insert(0, new PacketOutputFormatter());
});

// Configure Entity Framework Core with MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// Configure Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Connection"];
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});

// Configure health checks
var redisConnection = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddHealthChecks()
    .AddRedis(redisConnection, name: "redis",
        failureStatus: HealthStatus.Degraded);

// Register custom services
builder.Services.AddScoped<RequestContext>();
builder.Services.AddSingleton<DbLockService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<CacheService>();
builder.Services.AddScoped<UserService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Add Serilog request logging
app.UseSerilogRequestLogging();

// Add Prometheus HTTP metrics
app.UseHttpMetrics();

// Simple authentication middleware to extract userId from header
app.Use(async (context, next) =>
{
    var requestContext = context.RequestServices.GetRequiredService<RequestContext>();

    // Extract userId from header (for testing purposes)
    if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader)
        && ulong.TryParse(userIdHeader.FirstOrDefault(), out var userId))
    {
        requestContext.UserId = userId;
    }

    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        requestContext.SessionToken = authHeader.FirstOrDefault();
    }

    await next();
});

// Apply packet lock middleware
app.UsePacketLock();

// Health check endpoint (no lock required)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            }),
            timestamp = DateTime.UtcNow
        });
        await context.Response.WriteAsync(result);
    }
})
.WithName("HealthCheck")
.WithOpenApi();

// Test endpoint that requires lock
app.MapGet("/api/test/locked", async (RequestContext requestContext, ILogger<Program> logger) =>
{
    if (!requestContext.IsAuthenticated)
    {
        return Results.Unauthorized();
    }

    logger.LogInformation("Processing locked request for user {UserId}", requestContext.UserId);

    // Simulate some processing
    await Task.Delay(100);

    return Results.Ok(new
    {
        userId = requestContext.UserId,
        message = "This request was processed with DB lock",
        timestamp = DateTime.UtcNow
    });
})
.WithName("TestLocked")
.WithOpenApi();

// Test endpoint for concurrent lock testing
app.MapPost("/api/test/concurrent", async (
    RequestContext requestContext,
    DbLockService lockService,
    ILogger<Program> logger) =>
{
    if (!requestContext.IsAuthenticated)
    {
        return Results.Unauthorized();
    }

    var userId = requestContext.UserId;

    // Try to acquire lock and process
    var result = await lockService.ExecuteWithLockAsync(userId, async () =>
    {
        logger.LogInformation("Processing concurrent request for user {UserId}", userId);

        // Simulate processing time
        await Task.Delay(2000);

        return new
        {
            userId,
            message = "Concurrent request processed successfully",
            processedAt = DateTime.UtcNow
        };
    });

    if (result == null)
    {
        logger.LogWarning("Failed to acquire lock for user {UserId}", userId);
        return Results.Conflict(new { error = "LOCK_FAILED", message = "Could not acquire lock" });
    }

    return Results.Ok(result);
})
.WithName("TestConcurrent")
.WithOpenApi();

// Direct lock test endpoint (bypasses middleware)
app.MapPost("/api/test/direct-lock", async (
    ulong userId,
    DbLockService lockService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Testing direct lock for userId: {UserId}", userId);

    var lockAcquired = await lockService.AcquireLockAsync(userId);

    if (!lockAcquired)
    {
        return Results.Conflict(new { error = "LOCK_FAILED", message = "Could not acquire lock" });
    }

    try
    {
        await Task.Delay(1000); // Simulate work
        return Results.Ok(new { userId, message = "Lock acquired and released successfully" });
    }
    finally
    {
        await lockService.ReleaseLockAsync(userId);
    }
})
.WithName("TestDirectLock")
.WithOpenApi();

// ========== User CRUD Endpoints (Load → Process → Save) ==========

// Create user
app.MapPost("/api/users", async (User user, UserService userService) =>
{
    // 1. Load (none — new entity)
    // 2. Process (none — input as-is)
    // 3. Save
    var created = await userService.CreateAsync(user);
    return Results.Created($"/api/users/{created.UserId}", created);
})
.WithName("CreateUser")
.WithOpenApi();

// Get user by ID
app.MapGet("/api/users/{userId:long}", async (ulong userId, UserService userService) =>
{
    // 1. Load (cache → DB fallback)
    var user = await userService.GetByIdAsync(userId);
    return user is not null ? Results.Ok(user) : Results.NotFound();
})
.WithName("GetUser")
.WithOpenApi();

// Get all users
app.MapGet("/api/users", async (UserService userService) =>
{
    // 1. Load (cache TTL 30s → DB fallback)
    var users = await userService.GetAllAsync();
    return Results.Ok(users);
})
.WithName("GetAllUsers")
.WithOpenApi();

// Update user (with lock)
app.MapPut("/api/users/{userId:long}", async (
    ulong userId,
    User updatedUser,
    UserService userService,
    DbLockService lockService) =>
{
    var result = await lockService.ExecuteWithLockAsync(userId, async () =>
    {
        // 1. Load — DB direct (ChangeTracker tracked)
        var user = await userService.LoadAsync(userId);
        if (user is null)
            return Results.NotFound();

        // 2. Process — in-memory only
        user.Username = updatedUser.Username;
        user.Email = updatedUser.Email;
        user.Level = updatedUser.Level;
        user.Experience = updatedUser.Experience;
        user.Gold = updatedUser.Gold;

        // 3. Save — DB save + cache invalidation
        await userService.SaveAsync(user);
        return Results.Ok(user);
    });

    return result ?? Results.Conflict(new { error = "LOCK_FAILED",
        message = "Could not acquire lock for user update" });
})
.WithName("UpdateUser")
.WithOpenApi();

// Delete user
app.MapDelete("/api/users/{userId:long}", async (ulong userId, UserService userService) =>
{
    var deleted = await userService.DeleteAsync(userId);
    return deleted ? Results.NoContent() : Results.NotFound();
})
.WithName("DeleteUser")
.WithOpenApi();

// Add gold to user (with lock)
app.MapPost("/api/users/{userId:long}/add-gold", async (
    ulong userId,
    int amount,
    UserService userService,
    DbLockService lockService,
    ILogger<Program> logger) =>
{
    var result = await lockService.ExecuteWithLockAsync(userId, async () =>
    {
        // 1. Load — DB direct (ChangeTracker tracked)
        var user = await userService.LoadAsync(userId);
        if (user is null)
            return Results.NotFound(new { error = "User not found" });

        // 2. Process — in-memory only
        user.Gold += amount;

        // 3. Save — DB save + cache invalidation
        await userService.SaveAsync(user);

        logger.LogInformation("Added {Amount} gold to user {UserId}. New balance: {Gold}",
            amount, userId, user.Gold);
        return Results.Ok(new { userId, newGold = user.Gold, addedAmount = amount });
    });

    return result ?? Results.Conflict(new { error = "LOCK_FAILED" });
})
.WithName("AddGold")
.WithOpenApi();

// ========== Packet Serialization Endpoints ==========

// Test packet echo (supports JSON and MessagePack)
app.MapPost("/api/packet/echo", async (
    HttpContext context,
    ILogger<Program> logger) =>
{
    var contentType = context.Request.ContentType ?? "application/json";
    var accept = context.Request.Headers.Accept.ToString();

    logger.LogInformation("Packet echo request - ContentType: {ContentType}, Accept: {Accept}", contentType, accept);

    // Deserialize request based on Content-Type
    Packet<TestRequest>? request;
    if (contentType.Contains("application/x-msgpack"))
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        ms.Position = 0;
        request = MessagePack.MessagePackSerializer.Deserialize<Packet<TestRequest>>(ms);
        logger.LogInformation("Deserialized with MessagePack");
    }
    else
    {
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync();
        request = System.Text.Json.JsonSerializer.Deserialize<Packet<TestRequest>>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        logger.LogInformation("Deserialized with JSON");
    }

    if (request?.Data == null)
    {
        return Results.BadRequest(new { error = "Invalid packet format" });
    }

    // Create response packet
    var response = new Packet<TestResponse>
    {
        Code = 200,
        Message = "Success",
        Data = new TestResponse
        {
            Echo = request.Data.Name,
            ProcessedValue = request.Data.Value * 2,
            ServerTime = DateTime.UtcNow,
            SerializerType = accept.Contains("application/x-msgpack") ? "MessagePack" : "JSON"
        }
    };

    // Serialize response based on Accept header
    if (accept.Contains("application/x-msgpack"))
    {
        context.Response.ContentType = "application/x-msgpack";
        var bytes = MessagePack.MessagePackSerializer.Serialize(response);
        await context.Response.Body.WriteAsync(bytes);
        logger.LogInformation("Serialized response with MessagePack");
        return Results.Empty;
    }
    else
    {
        logger.LogInformation("Serialized response with JSON");
        return Results.Ok(response);
    }
})
.WithName("PacketEcho")
.WithOpenApi();

// User packet test (supports JSON and MessagePack)
app.MapPost("/api/packet/user", async (
    HttpContext context,
    AppDbContext dbContext,
    ILogger<Program> logger) =>
{
    var contentType = context.Request.ContentType ?? "application/json";
    var accept = context.Request.Headers.Accept.ToString();

    // Deserialize UserPacket
    Packet<UserPacket>? request;
    if (contentType.Contains("application/x-msgpack"))
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        ms.Position = 0;
        request = MessagePack.MessagePackSerializer.Deserialize<Packet<UserPacket>>(ms);
    }
    else
    {
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync();
        request = System.Text.Json.JsonSerializer.Deserialize<Packet<UserPacket>>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    if (request?.Data == null)
    {
        return Results.BadRequest(new { error = "Invalid packet format" });
    }

    // Save user to database
    var user = new User
    {
        UserId = request.Data.UserId,
        Username = request.Data.Username,
        Email = request.Data.Email,
        Level = request.Data.Level,
        Gold = request.Data.Gold,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();

    logger.LogInformation("User created via packet: {UserId}", user.UserId);

    // Create response
    var response = new Packet<UserPacket>
    {
        Code = 201,
        Message = "User created successfully",
        Data = new UserPacket
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            Level = user.Level,
            Gold = user.Gold
        }
    };

    // Serialize response
    if (accept.Contains("application/x-msgpack"))
    {
        context.Response.ContentType = "application/x-msgpack";
        context.Response.StatusCode = 201;
        var bytes = MessagePack.MessagePackSerializer.Serialize(response);
        await context.Response.Body.WriteAsync(bytes);
        return Results.Empty;
    }
    else
    {
        return Results.Created($"/api/users/{user.UserId}", response);
    }
})
.WithName("PacketUser")
.WithOpenApi();

// Map controllers for formatter support
app.MapControllers();

// Map Prometheus metrics endpoint
app.MapMetrics();

app.Run();
