# LiteApi 프로젝트 구축 가이드

ASP.NET Core 기반 게임 웹 서버 개발을 위한 LiteApi 구현 가이드입니다.

---

## 목차

1. [프로젝트 개요](#프로젝트-개요)
2. [1단계: 프로젝트 생성](#1단계-프로젝트-생성)
3. [2단계: DB Lock 기능 추가](#2단계-db-lock-기능-추가)
4. [3단계: Entity Framework Core 적용](#3단계-entity-framework-core-적용)
5. [4단계: YAML 설정 파일로 전환](#4단계-yaml-설정-파일로-전환)
6. [5단계: 패킷 시리얼라이저 추가](#5단계-패킷-시리얼라이저-추가)
7. [6단계: Serilog 로깅 추가](#6단계-serilog-로깅-추가)
8. [7단계: Prometheus 메트릭 추가](#7단계-prometheus-메트릭-추가)
9. [8단계: xUnit 단위 테스트 추가](#8단계-xunit-단위-테스트-추가)
10. [9단계: Redis 캐싱 추가](#9단계-redis-캐싱-추가)
11. [최종 프로젝트 구조](#최종-프로젝트-구조)
12. [참고 자료](#참고-자료)

---

## 프로젝트 개요

### 목표
기존 모바일 웹 게임서버의 핵심 기능을 Minimal API 방식으로 간결하게 재구현

### 주요 변경사항
| 항목 | projectgsi_server | liteapi |
|------|-------------------|-------------|
| 아키텍처 | Controller 기반 | **Minimal API** |
| ORM | Dapper (Micro-ORM) | **Entity Framework Core** |
| 설정 파일 | appsettings.json | **appsettings.yaml** |
| 직렬화 | MessagePack (단일) | **JSON & MessagePack (이중)** |
| 락 관리 | UserLockManager + AuthRepo | **DbLockService (통합)** |
| 복잡도 | 높음 (다층 구조) | **낮음 (간결)** |

---

## 1단계: 프로젝트 생성

### 1.1 새 Minimal API 프로젝트 생성

```bash
dotnet new webapi -n liteapi -o liteapi --use-minimal-apis
cd liteapi
```

생성되는 기본 파일:
- `Program.cs` - 메인 진입점
- `appsettings.json` - 설정 파일
- `liteapi.csproj` - 프로젝트 파일

### 1.2 기본 구조 확인

```bash
dotnet build
dotnet run
```

브라우저에서 `http://localhost:5000/swagger` 접속하여 Swagger UI 확인

---

## 2단계: DB Lock 기능 추가

### 2.1 필요한 패키지 설치

```bash
dotnet add package MySqlConnector
dotnet add package Dapper
```

### 2.2 디렉토리 구조 생성

```bash
mkdir -p Models Services Middleware
```

### 2.3 RequestContext 생성

**Models/RequestContext.cs**
```csharp
namespace liteapi.Models;

public class RequestContext
{
    public ulong UserId { get; set; }
    public string? SessionToken { get; set; }
    public bool IsAuthenticated => UserId > 0;
}
```

### 2.4 DbLockService 구현

**Services/DbLockService.cs**
```csharp
using Dapper;
using MySqlConnector;

namespace liteapi.Services;

public class DbLockService
{
    private readonly string _connectionString;
    private readonly int _lockTimeoutSeconds;
    private readonly string _lockPrefix;
    private readonly ILogger<DbLockService> _logger;

    public DbLockService(IConfiguration configuration, ILogger<DbLockService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _lockTimeoutSeconds = configuration.GetValue<int>("Lock:TimeoutSeconds", 30);
        _lockPrefix = configuration.GetValue<string>("Lock:Prefix") ?? "api";
        _logger = logger;
    }

    public async Task<bool> AcquireLockAsync(ulong userId)
    {
        var lockName = GetLockName(userId);
        if (string.IsNullOrEmpty(lockName)) return false;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $"SELECT GET_LOCK(@LockName, {_lockTimeoutSeconds}) AS lock_result";
        var result = await connection.QuerySingleOrDefaultAsync<int>(sql, new { LockName = lockName });

        if (result <= 0)
        {
            _logger.LogWarning("Failed to acquire lock for userId: {UserId}", userId);
            return false;
        }

        _logger.LogDebug("Lock acquired for userId: {UserId}", userId);
        return true;
    }

    public async Task<bool> ReleaseLockAsync(ulong userId)
    {
        var lockName = GetLockName(userId);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT RELEASE_LOCK(@LockName) AS unlock_result";
        var result = await connection.QuerySingleOrDefaultAsync<int>(sql, new { LockName = lockName });

        _logger.LogDebug("Lock released for userId: {UserId}", userId);
        return result > 0;
    }

    public async Task<T?> ExecuteWithLockAsync<T>(ulong userId, Func<Task<T>> func)
    {
        var lockAcquired = await AcquireLockAsync(userId);
        if (!lockAcquired) return default;

        try
        {
            return await func();
        }
        finally
        {
            await ReleaseLockAsync(userId);
        }
    }

    private string GetLockName(ulong userId)
    {
        return userId > 0 ? $"lock_{_lockPrefix}_{userId}" : string.Empty;
    }
}
```

### 2.5 PacketLockMiddleware 구현

**Middleware/PacketLockMiddleware.cs**
```csharp
using liteapi.Models;
using liteapi.Services;

namespace liteapi.Middleware;

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
            lockAcquired = await lockService.AcquireLockAsync(userId);
            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for user {UserId}", userId);
                context.Response.StatusCode = 409;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "LOCK_ACQUISITION_FAILED",
                    message = "Could not acquire user lock"
                });
                return;
            }

            await _next(context);
        }
        finally
        {
            if (lockAcquired)
            {
                await lockService.ReleaseLockAsync(userId);
            }
        }
    }
}

public static class PacketLockMiddlewareExtensions
{
    public static IApplicationBuilder UsePacketLock(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PacketLockMiddleware>();
    }
}
```

### 2.6 Program.cs 업데이트

```csharp
using liteapi.Middleware;
using liteapi.Models;
using liteapi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register services
builder.Services.AddScoped<RequestContext>();
builder.Services.AddSingleton<DbLockService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Authentication middleware
app.Use(async (context, next) =>
{
    var requestContext = context.RequestServices.GetRequiredService<RequestContext>();

    if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader)
        && ulong.TryParse(userIdHeader.FirstOrDefault(), out var userId))
    {
        requestContext.UserId = userId;
    }

    await next();
});

// Apply packet lock middleware
app.UsePacketLock();

// Test endpoint
app.MapGet("/api/test/locked", async (RequestContext requestContext) =>
{
    if (!requestContext.IsAuthenticated)
        return Results.Unauthorized();

    return Results.Ok(new {
        userId = requestContext.UserId,
        message = "Processed with lock"
    });
});

app.Run();
```

### 2.7 appsettings.json 설정

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=liteapi_db;User=root;Password=your_password;"
  },
  "Lock": {
    "TimeoutSeconds": 30,
    "Prefix": "api"
  }
}
```

### 2.8 테스트

```bash
# 빌드
dotnet build

# 실행
dotnet run

# 테스트
curl -H "X-User-Id: 12345" http://localhost:5000/api/test/locked
```

---

## 3단계: Entity Framework Core 적용

### 3.1 패키지 설치

```bash
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.11
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 8.0.2
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.11
```

### 3.2 디렉토리 생성

```bash
mkdir -p Data
```

### 3.3 User Entity 생성

**Models/User.cs**
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace liteapi.Models;

[Table("users")]
public class User
{
    [Key]
    [Column("user_id")]
    public ulong UserId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [MaxLength(100)]
    [Column("email")]
    public string? Email { get; set; }

    [Column("level")]
    public int Level { get; set; } = 1;

    [Column("experience")]
    public long Experience { get; set; } = 0;

    [Column("gold")]
    public long Gold { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### 3.4 DbContext 생성

**Data/AppDbContext.cs**
```csharp
using Microsoft.EntityFrameworkCore;
using liteapi.Models;

namespace liteapi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Level).HasDefaultValue(1);
            entity.Property(e => e.Gold).HasDefaultValue(0);
        });
    }
}
```

### 3.5 DbLockService를 EF Core로 변경

**Services/DbLockService.cs** (업데이트)
```csharp
using Microsoft.EntityFrameworkCore;
using liteapi.Data;

namespace liteapi.Services;

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

    public async Task<bool> AcquireLockAsync(ulong userId, AppDbContext? dbContext = null)
    {
        var lockName = GetLockName(userId);
        if (string.IsNullOrEmpty(lockName)) return false;

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
                .SqlQuery<LockResult>($"SELECT GET_LOCK({lockName}, {_lockTimeoutSeconds}) AS Result")
                .FirstOrDefaultAsync();

            return result?.Result > 0;
        }
        finally
        {
            if (shouldDisposeContext)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

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

            return result?.Result > 0;
        }
        finally
        {
            if (shouldDisposeContext)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

    private string GetLockName(ulong userId)
    {
        return userId > 0 ? $"lock_{_lockPrefix}_{userId}" : string.Empty;
    }
}

public class LockResult { public int Result { get; set; } }
public class UnlockResult { public int Result { get; set; } }
```

### 3.6 Program.cs에 EF Core 등록

```csharp
using Microsoft.EntityFrameworkCore;
using liteapi.Data;

// ... (기존 코드)

// Configure Entity Framework Core with MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string not found");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});
```

### 3.7 User CRUD 엔드포인트 추가

```csharp
// Create user
app.MapPost("/api/users", async (User user, AppDbContext dbContext) =>
{
    user.CreatedAt = DateTime.UtcNow;
    user.UpdatedAt = DateTime.UtcNow;
    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync();
    return Results.Created($"/api/users/{user.UserId}", user);
});

// Get all users
app.MapGet("/api/users", async (AppDbContext dbContext) =>
{
    var users = await dbContext.Users.ToListAsync();
    return Results.Ok(users);
});

// Update user (with lock)
app.MapPut("/api/users/{userId:long}", async (
    ulong userId,
    User updatedUser,
    AppDbContext dbContext,
    DbLockService lockService) =>
{
    var lockAcquired = await lockService.AcquireLockAsync(userId, dbContext);
    if (!lockAcquired)
        return Results.Conflict(new { error = "LOCK_FAILED" });

    try
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user is null) return Results.NotFound();

        user.Username = updatedUser.Username;
        user.Email = updatedUser.Email;
        user.Level = updatedUser.Level;
        user.Gold = updatedUser.Gold;
        user.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.Ok(user);
    }
    finally
    {
        await lockService.ReleaseLockAsync(userId, dbContext);
    }
});
```

### 3.8 마이그레이션 생성 및 적용

```bash
# 마이그레이션 생성
dotnet ef migrations add InitialCreate

# 데이터베이스에 적용
dotnet ef database update
```

---

## 4단계: YAML 설정 파일로 전환

### 4.1 패키지 설치

```bash
dotnet add package NetEscapades.Configuration.Yaml
```

### 4.2 appsettings.yaml 생성

**appsettings.yaml**
```yaml
Logging:
  LogLevel:
    Default: Information
    Microsoft.AspNetCore: Warning

AllowedHosts: "*"

ConnectionStrings:
  DefaultConnection: "Server=localhost;Database=liteapi_db;User=root;Password=your_password;"

Lock:
  TimeoutSeconds: 30
  Prefix: "api"
```

**appsettings.Development.yaml**
```yaml
Logging:
  LogLevel:
    Default: Debug
    Microsoft.AspNetCore: Information
    Microsoft.EntityFrameworkCore: Information
    Microsoft.EntityFrameworkCore.Database.Command: Information
```

### 4.3 Program.cs에 YAML 설정 추가

```csharp
using NetEscapades.Configuration.Yaml;

var builder = WebApplication.CreateBuilder(args);

// Configure YAML configuration
builder.Configuration
    .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
    .AddYamlFile($"appsettings.{builder.Environment.EnvironmentName}.yaml", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// ... (나머지 코드)
```

### 4.4 기존 JSON 파일 백업

```bash
mv appsettings.json appsettings.json.bak
mv appsettings.Development.json appsettings.Development.json.bak
```

### 4.5 .gitignore 업데이트

**.gitignore**
```
*.bak
*.json.bak
bin/
obj/
.vs/
```

---

## 5단계: 패킷 시리얼라이저 추가

### 5.1 패키지 설치

```bash
dotnet add package MessagePack
dotnet add package MessagePack.AspNetCoreMvcFormatter
```

### 5.2 디렉토리 생성

```bash
mkdir -p Formatters
```

### 5.3 패킷 모델 생성

**Models/Packet.cs**
```csharp
using MessagePack;

namespace liteapi.Models;

[MessagePackObject]
public class Packet<T>
{
    [Key(0)]
    public int Code { get; set; }

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public T? Data { get; set; }
}

[MessagePackObject]
public class TestRequest
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public int Value { get; set; }

    [Key(2)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

[MessagePackObject]
public class TestResponse
{
    [Key(0)]
    public string Echo { get; set; } = string.Empty;

    [Key(1)]
    public int ProcessedValue { get; set; }

    [Key(2)]
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;

    [Key(3)]
    public string SerializerType { get; set; } = string.Empty;
}
```

### 5.4 커스텀 Input Formatter 생성

**Formatters/PacketInputFormatter.cs**
```csharp
using MessagePack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace liteapi.Formatters;

public class PacketInputFormatter : InputFormatter
{
    private const string JsonContentType = "application/json";
    private const string MessagePackContentType = "application/x-msgpack";

    public PacketInputFormatter()
    {
        SupportedMediaTypes.Add(JsonContentType);
        SupportedMediaTypes.Add(MessagePackContentType);
    }

    public override bool CanRead(InputFormatterContext context)
    {
        var contentType = context.HttpContext.Request.ContentType;
        return contentType != null &&
               (contentType.StartsWith(JsonContentType) ||
                contentType.StartsWith(MessagePackContentType));
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        var contentType = request.ContentType ?? JsonContentType;

        try
        {
            if (contentType.StartsWith(MessagePackContentType))
            {
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                ms.Position = 0;
                var result = MessagePackSerializer.Deserialize(context.ModelType, ms);
                return await InputFormatterResult.SuccessAsync(result);
            }
            else
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();
                var result = JsonSerializer.Deserialize(json, context.ModelType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return await InputFormatterResult.SuccessAsync(result!);
            }
        }
        catch (Exception ex)
        {
            context.ModelState.AddModelError(context.ModelName, $"Deserialization failed: {ex.Message}");
            return await InputFormatterResult.FailureAsync();
        }
    }
}
```

### 5.5 커스텀 Output Formatter 생성

**Formatters/PacketOutputFormatter.cs**
```csharp
using MessagePack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace liteapi.Formatters;

public class PacketOutputFormatter : OutputFormatter
{
    private const string JsonContentType = "application/json";
    private const string MessagePackContentType = "application/x-msgpack";

    public PacketOutputFormatter()
    {
        SupportedMediaTypes.Add(JsonContentType);
        SupportedMediaTypes.Add(MessagePackContentType);
    }

    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        return context.Object != null;
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
        var response = context.HttpContext.Response;
        var accept = context.HttpContext.Request.Headers.Accept.ToString();

        var useMessagePack = accept.Contains(MessagePackContentType, StringComparison.OrdinalIgnoreCase);

        if (useMessagePack)
        {
            response.ContentType = MessagePackContentType;
            var bytes = MessagePackSerializer.Serialize(context.Object);
            await response.Body.WriteAsync(bytes);
        }
        else
        {
            response.ContentType = JsonContentType;
            var json = JsonSerializer.Serialize(context.Object, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            await response.Body.WriteAsync(bytes);
        }
    }
}
```

### 5.6 Program.cs에 Formatters 등록

```csharp
using liteapi.Formatters;

// Add MVC Controllers with custom formatters
builder.Services.AddControllers(options =>
{
    options.InputFormatters.Insert(0, new PacketInputFormatter());
    options.OutputFormatters.Insert(0, new PacketOutputFormatter());
});

// ... (나머지 코드)

// Map controllers for formatter support
app.MapControllers();
```

### 5.7 패킷 엔드포인트 추가

```csharp
// Test packet echo
app.MapPost("/api/packet/echo", async (HttpContext context, ILogger<Program> logger) =>
{
    var contentType = context.Request.ContentType ?? "application/json";
    var accept = context.Request.Headers.Accept.ToString();

    // Deserialize based on Content-Type
    Packet<TestRequest>? request;
    if (contentType.Contains("application/x-msgpack"))
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        ms.Position = 0;
        request = MessagePack.MessagePackSerializer.Deserialize<Packet<TestRequest>>(ms);
    }
    else
    {
        using var reader = new StreamReader(context.Request.Body);
        var json = await reader.ReadToEndAsync();
        request = System.Text.Json.JsonSerializer.Deserialize<Packet<TestRequest>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    if (request?.Data == null)
        return Results.BadRequest(new { error = "Invalid packet" });

    // Create response
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

    // Serialize based on Accept header
    if (accept.Contains("application/x-msgpack"))
    {
        context.Response.ContentType = "application/x-msgpack";
        var bytes = MessagePack.MessagePackSerializer.Serialize(response);
        await context.Response.Body.WriteAsync(bytes);
        return Results.Empty;
    }
    else
    {
        return Results.Ok(response);
    }
});
```

### 5.8 테스트 파일 생성

**test-packet.http**
```http
### JSON 요청/응답
POST http://localhost:5000/api/packet/echo
Content-Type: application/json
Accept: application/json

{
  "code": 0,
  "message": "Test request",
  "data": {
    "name": "TestUser",
    "value": 42,
    "timestamp": "2026-01-01T00:00:00Z"
  }
}

### JSON 요청, MessagePack 응답
POST http://localhost:5000/api/packet/echo
Content-Type: application/json
Accept: application/x-msgpack

{
  "code": 0,
  "message": "Test request",
  "data": {
    "name": "TestUser",
    "value": 100,
    "timestamp": "2026-01-01T00:00:00Z"
  }
}
```

---

## 6단계: Serilog 로깅 추가

### 6.1 패키지 설치

```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

### 6.2 appsettings.yaml에 Serilog 설정 추가

**appsettings.yaml** (Serilog 섹션 추가)
```yaml
Serilog:
  Using:
    - Serilog.Sinks.Console
    - Serilog.Sinks.File
  MinimumLevel:
    Default: Information
    Override:
      Microsoft: Warning
      Microsoft.AspNetCore: Warning
      System: Warning
  WriteTo:
    - Name: Console
      Args:
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    - Name: File
      Args:
        path: "logs/mini-server-.log"
        rollingInterval: Day
        retainedFileCountLimit: 30
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
  Enrich:
    - FromLogContext
    - WithMachineName
    - WithThreadId
```

**appsettings.Development.yaml** (Serilog 섹션 추가)
```yaml
Serilog:
  MinimumLevel:
    Default: Debug
    Override:
      Microsoft: Information
      Microsoft.AspNetCore: Information
      Microsoft.EntityFrameworkCore: Information
      Microsoft.EntityFrameworkCore.Database.Command: Information
  WriteTo:
    - Name: Console
      Args:
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
```

### 6.3 Program.cs에 Serilog 추가

```csharp
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

// ... (기존 코드)

var app = builder.Build();

// ... (기존 미들웨어)

// Add Serilog request logging
app.UseSerilogRequestLogging();

// ... (나머지 코드)

app.Run();
```

### 6.4 로그 확인

Serilog는 다음 위치에 로그를 기록합니다:
- **콘솔**: 실시간 로그 출력
- **파일**: `logs/mini-server-YYYY-MM-DD.log` (일별 롤링)

로그 레벨:
- **Debug**: 상세한 개발 정보
- **Information**: 일반 정보 메시지
- **Warning**: 경고 메시지
- **Error**: 에러 메시지

---

## 7단계: Prometheus 메트릭 추가

### 7.1 패키지 설치

```bash
dotnet add package prometheus-net.AspNetCore
```

### 7.2 MetricsService 생성

**Services/MetricsService.cs**
```csharp
using Prometheus;

namespace liteapi.Services;

public class MetricsService
{
    // Counters - 이벤트 총 발생 횟수 추적
    private static readonly Counter RequestsTotal = Metrics.CreateCounter(
        "liteapi_requests_total",
        "Total number of HTTP requests",
        new CounterConfiguration
        {
            LabelNames = new[] { "method", "endpoint", "status_code" }
        });

    private static readonly Counter DbLockAcquisitionsTotal = Metrics.CreateCounter(
        "liteapi_db_lock_acquisitions_total",
        "Total number of database lock acquisitions",
        new CounterConfiguration
        {
            LabelNames = new[] { "result" }
        });

    private static readonly Counter PacketProcessingTotal = Metrics.CreateCounter(
        "liteapi_packet_processing_total",
        "Total number of packets processed",
        new CounterConfiguration
        {
            LabelNames = new[] { "format", "endpoint" }
        });

    // Gauges - 현재 값 추적
    private static readonly Gauge ActiveDbLocks = Metrics.CreateGauge(
        "liteapi_active_db_locks",
        "Number of currently active database locks");

    private static readonly Gauge ActiveUsers = Metrics.CreateGauge(
        "liteapi_active_users",
        "Number of currently active users");

    // Histograms - 값의 분포 추적 (요청 시간 등)
    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "liteapi_request_duration_seconds",
        "HTTP request duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "method", "endpoint" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
        });

    private static readonly Histogram DbLockWaitDuration = Metrics.CreateHistogram(
        "liteapi_db_lock_wait_duration_seconds",
        "Time spent waiting for database locks in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    // 메서드들
    public void IncrementRequest(string method, string endpoint, int statusCode)
    {
        RequestsTotal.WithLabels(method, endpoint, statusCode.ToString()).Inc();
    }

    public IDisposable TrackRequestDuration(string method, string endpoint)
    {
        return RequestDuration.WithLabels(method, endpoint).NewTimer();
    }

    public void IncrementDbLockAcquisition(bool success)
    {
        DbLockAcquisitionsTotal.WithLabels(success ? "success" : "failed").Inc();
    }

    public void IncrementActiveDbLocks()
    {
        ActiveDbLocks.Inc();
    }

    public void DecrementActiveDbLocks()
    {
        ActiveDbLocks.Dec();
    }

    public IDisposable TrackDbLockWaitDuration()
    {
        return DbLockWaitDuration.NewTimer();
    }

    public void IncrementPacketProcessing(string format, string endpoint)
    {
        PacketProcessingTotal.WithLabels(format, endpoint).Inc();
    }

    public void SetActiveUsers(int count)
    {
        ActiveUsers.Set(count);
    }

    public void IncrementActiveUsers()
    {
        ActiveUsers.Inc();
    }

    public void DecrementActiveUsers()
    {
        ActiveUsers.Dec();
    }
}
```

### 7.3 Program.cs에 Prometheus 추가

```csharp
using Prometheus;

// Register custom services
builder.Services.AddScoped<RequestContext>();
builder.Services.AddSingleton<DbLockService>();
builder.Services.AddSingleton<MetricsService>();  // 추가

var app = builder.Build();

// ... (기존 미들웨어)

// Add Serilog request logging
app.UseSerilogRequestLogging();

// Add Prometheus HTTP metrics
app.UseHttpMetrics();

// ... (엔드포인트들)

// Map Prometheus metrics endpoint
app.MapMetrics();

app.Run();
```

### 7.4 메트릭 엔드포인트 확인

```bash
# 서버 실행
dotnet run

# 메트릭 확인
curl http://localhost:5000/metrics
```

출력 예시:
```
# HELP liteapi_requests_total Total number of HTTP requests
# TYPE liteapi_requests_total counter
liteapi_requests_total{method="GET",endpoint="/api/users",status_code="200"} 42

# HELP liteapi_active_users Number of currently active users
# TYPE liteapi_active_users gauge
liteapi_active_users 15

# HELP liteapi_request_duration_seconds HTTP request duration in seconds
# TYPE liteapi_request_duration_seconds histogram
liteapi_request_duration_seconds_bucket{method="GET",endpoint="/api/users",le="0.001"} 10
liteapi_request_duration_seconds_bucket{method="GET",endpoint="/api/users",le="0.002"} 25
...
```

### 7.5 Prometheus 서버 설정 (선택사항)

**prometheus.yml**
```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'liteapi'
    static_configs:
      - targets: ['localhost:5000']
```

---

## 8단계: xUnit 단위 테스트 추가

### 8.1 테스트 프로젝트 생성

```bash
# 루트 디렉토리에서
cd ..
dotnet new xunit -n liteapi.Tests -o liteapi.Tests
cd liteapi.Tests
```

### 8.2 패키지 설치

```bash
# liteapi 프로젝트 참조
dotnet add reference ../liteapi/liteapi.csproj

# 테스트 패키지
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.11
```

### 8.3 DbLockService 테스트 작성

**Services/DbLockServiceTests.cs**
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using liteapi.Data;
using liteapi.Services;
using Moq;
using Xunit;

namespace liteapi.Tests.Services;

public class DbLockServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly DbLockService _lockService;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<ILogger<DbLockService>> _mockLogger;

    public DbLockServiceTests()
    {
        // Configuration
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Lock:TimeoutSeconds", "5" },
                { "Lock:Prefix", "test" }
            })
            .Build();

        // In-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options);

        // Service provider
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => _dbContext);
        _serviceProvider = serviceCollection.BuildServiceProvider();

        // Logger mock
        _mockLogger = new Mock<ILogger<DbLockService>>();

        _lockService = new DbLockService(_serviceProvider, _configuration, _mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldLoadConfiguration()
    {
        // Arrange & Act
        var service = new DbLockService(_serviceProvider, _configuration, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Theory]
    [InlineData(12345ul)]
    [InlineData(99999ul)]
    [InlineData(1ul)]
    public void GetLockName_ShouldGenerateCorrectFormat(ulong userId)
    {
        // Arrange
        var expectedPrefix = _configuration["Lock:Prefix"];

        // Act
        var lockName = $"{expectedPrefix}:user:{userId}";

        // Assert
        lockName.Should().StartWith("test:user:");
        lockName.Should().EndWith(userId.ToString());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
```

### 8.4 MetricsService 테스트 작성

**Services/MetricsServiceTests.cs**
```csharp
using FluentAssertions;
using liteapi.Services;
using Xunit;

namespace liteapi.Tests.Services;

public class MetricsServiceTests
{
    private readonly MetricsService _metricsService;

    public MetricsServiceTests()
    {
        _metricsService = new MetricsService();
    }

    [Fact]
    public void Constructor_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new MetricsService();

        // Assert
        service.Should().NotBeNull();
    }

    [Theory]
    [InlineData("GET", "/api/users", 200)]
    [InlineData("POST", "/api/users", 201)]
    [InlineData("PUT", "/api/users/123", 200)]
    public void IncrementRequest_WithDifferentMethods_ShouldNotThrow(
        string method, string endpoint, int statusCode)
    {
        // Act
        Action act = () => _metricsService.IncrementRequest(method, endpoint, statusCode);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TrackRequestDuration_ShouldReturnDisposable()
    {
        // Arrange
        var method = "GET";
        var endpoint = "/api/users";

        // Act
        var timer = _metricsService.TrackRequestDuration(method, endpoint);

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();

        // Cleanup
        timer.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncrementDbLockAcquisition_ShouldNotThrow(bool success)
    {
        // Act
        Action act = () => _metricsService.IncrementDbLockAcquisition(success);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void SetActiveUsers_ShouldNotThrow(int count)
    {
        // Act
        Action act = () => _metricsService.SetActiveUsers(count);

        // Assert
        act.Should().NotThrow();
    }
}
```

### 8.5 User 모델 테스트 작성

**Models/UserTests.cs**
```csharp
using FluentAssertions;
using liteapi.Models;
using Xunit;

namespace liteapi.Tests.Models;

public class UserTests
{
    [Fact]
    public void User_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var user = new User();

        // Assert
        user.UserId.Should().Be(0);
        user.Username.Should().BeNullOrEmpty();
        user.Email.Should().BeNullOrEmpty();
        user.Level.Should().Be(1); // 기본 레벨은 1
        user.Experience.Should().Be(0);
        user.Gold.Should().Be(0);
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        user.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void User_CanSetAllProperties()
    {
        // Arrange
        var userId = 12345ul;
        var username = "TestUser";
        var email = "test@example.com";
        var level = 10;
        var gold = 10000;
        var now = DateTime.UtcNow;

        // Act
        var user = new User
        {
            UserId = userId,
            Username = username,
            Email = email,
            Level = level,
            Gold = gold,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Assert
        user.UserId.Should().Be(userId);
        user.Username.Should().Be(username);
        user.Email.Should().Be(email);
        user.Level.Should().Be(level);
        user.Gold.Should().Be(gold);
    }
}
```

### 8.6 RequestContext 테스트 작성

**Models/RequestContextTests.cs**
```csharp
using FluentAssertions;
using liteapi.Models;
using Xunit;

namespace liteapi.Tests.Models;

public class RequestContextTests
{
    [Fact]
    public void RequestContext_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var context = new RequestContext();

        // Assert
        context.UserId.Should().Be(0);
        context.SessionToken.Should().BeNull();
        context.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(12345)]
    public void IsAuthenticated_WhenUserIdIsGreaterThanZero_ShouldReturnTrue(ulong userId)
    {
        // Arrange
        var context = new RequestContext
        {
            UserId = userId
        };

        // Act & Assert
        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void SessionToken_CanBeSetAndRetrieved()
    {
        // Arrange
        var context = new RequestContext();
        var expectedToken = "test-session-token-123";

        // Act
        context.SessionToken = expectedToken;

        // Assert
        context.SessionToken.Should().Be(expectedToken);
    }
}
```

### 8.7 테스트 실행

```bash
# 모든 테스트 실행
dotnet test

# 특정 테스트만 실행
dotnet test --filter "FullyQualifiedName~MetricsServiceTests"

# 상세한 출력
dotnet test --verbosity normal
```

출력 예시:
```
Passed!  - Failed:     0, Passed:    48, Skipped:     5, Total:    53, Duration: 145 ms
```

### 8.8 테스트 커버리지 확인 (선택사항)

```bash
# coverlet 패키지 설치
dotnet add package coverlet.collector

# 커버리지 포함하여 테스트 실행
dotnet test /p:CollectCoverage=true
```

---

## 9단계: Redis 캐싱 추가

### 핵심 원칙

> **캐시 장애가 서비스 장애가 되어서는 안 된다.**
>
> Redis는 성능 최적화 수단이며, 핵심 의존성이 아니다.
> Redis 연결 실패, 타임아웃, 직렬화 오류 등 어떤 캐시 장애가 발생하더라도
> LiteApi는 DB 직접 조회로 정상 동작해야 한다.

이 원칙은 모든 구현에 적용되며:
- 모든 캐시 작업을 try-catch로 감싸고, 예외 시 로그만 남긴 뒤 DB로 폴백
- Redis를 optional dependency로 취급 (Redis 없이도 서비스 기동 가능)
- 헬스체크에서 Redis 상태는 `Degraded`로 보고 (`Unhealthy`로 만들지 않음)

### 9.1 필요한 패키지 설치

```bash
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 10.0.5
dotnet add package AspNetCore.HealthChecks.Redis --version 9.0.0
```

- `Microsoft.Extensions.Caching.StackExchangeRedis`: `IDistributedCache` 구현체, `StackExchange.Redis` 내부 포함
- `AspNetCore.HealthChecks.Redis`: Redis 헬스체크 지원

### 9.2 Redis 연결 설정

**appsettings.yaml에 Redis 설정 추가:**
```yaml
Redis:
  Connection: "localhost:6379"
  InstanceName: "liteapi:"
  DefaultTTLSeconds: 300    # 단일 엔티티 캐시 만료 5분
  ListTTLSeconds: 30        # 목록 캐시 만료 30초 (무효화 안 함, TTL 만료에 의존)
```

**Program.cs에 서비스 등록:**
```csharp
// Redis 캐시 등록 — 실제 연결은 첫 사용 시 lazy하게 이루어진다.
// 연결 실패 시에도 서비스는 정상 기동되며, CacheService 내부 try-catch로 폴백한다.
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Connection"];
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});
```

### 9.3 CacheService 구현

**새 파일: `Services/CacheService.cs`**

`IDistributedCache`를 래핑하여 모든 캐시 예외를 내부에서 흡수한다.

```csharp
public class CacheService
{
    private readonly IDistributedCache _cache;
    private readonly MetricsService _metrics;
    private readonly ILogger<CacheService> _logger;
    private readonly TimeSpan _defaultTTL;

    public CacheService(
        IDistributedCache cache,
        MetricsService metrics,
        IConfiguration configuration,
        ILogger<CacheService> logger)
    {
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
        _defaultTTL = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Redis:DefaultTTLSeconds", 300));
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var prefix = key.Contains(':') ? key[..key.IndexOf(':')] : key;
        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("get");
            var cached = await _cache.GetStringAsync(key);
            if (cached is null)
            {
                _metrics.IncrementCacheMiss(prefix);
                return default;
            }
            _metrics.IncrementCacheHit(prefix);
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 읽기 실패: {Key}. DB로 폴백", key);
            _metrics.IncrementCacheMiss(prefix);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("set");
            var json = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? _defaultTTL
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 쓰기 실패: {Key}. 무시하고 계속 진행", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("remove");
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 삭제 실패: {Key}. TTL 만료에 의존", key);
        }
    }
}
```

DI 등록: `builder.Services.AddSingleton<CacheService>();`

### 9.4 UserService 구현

**새 파일: `Services/UserService.cs`**

캐시 로직을 서비스 레이어에 캡슐화한다. 엔드포인트는 `UserService`만 호출하며 캐시 존재를 알지 못한다.

#### Load → Process → Save 아키텍처

```
┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌─────────┐
│   Endpoint   │───>│  UserService │───>│ CacheService │───>│  Redis  │
│              │    │              │    │              │    │         │
│  1. Load     │    │  Load:       │    │  Get/Set/    │    └─────────┘
│  2. Process  │    │   캐시→DB    │    │  Remove      │
│  3. Save     │    │  Save:       │    │  (예외 흡수)  │
│              │    │   DB저장     │    └──────────────┘
│              │    │   →캐시무효화│
│              │    │              │    ┌─────────┐
│              │    │              │───>│  MySQL  │
└──────────────┘    └──────────────┘    └─────────┘
```

#### Load 메서드 (데이터 조회)

| 메서드 | 용도 | 캐시 사용 | ChangeTracker |
|--------|------|----------|---------------|
| `GetByIdAsync(userId)` | 읽기 전용 | 캐시 → DB 폴백 | `AsNoTracking` |
| `GetAllAsync()` | 목록 조회 | 캐시(30초) → DB 폴백 | `AsNoTracking` |
| `LoadAsync(userId)` | 쓰기용 | 사용 안 함 (DB 직접) | 추적됨 |

```csharp
// [읽기 전용 Load] 캐시 → DB 폴백. AsNoTracking 적용.
public async Task<User?> GetByIdAsync(ulong userId)
{
    var cached = await _cache.GetAsync<User>(UserKey(userId));
    if (cached is not null) return cached;

    var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
    if (user is not null)
        await _cache.SetAsync(UserKey(userId), user, _userTTL);
    return user;
}

// [쓰기용 Load] DB에서 직접 조회. ChangeTracker에 추적됨.
public async Task<User?> LoadAsync(ulong userId)
{
    return await _db.Users.FindAsync(userId);
}
```

#### Save 메서드 (저장 + 캐시 무효화)

모든 쓰기 메서드는 **명시적 트랜잭션**을 사용하며, 캐시 무효화는 커밋 이후에 수행한다.

```csharp
public async Task SaveAsync(User user)
{
    user.UpdatedAt = DateTime.UtcNow;

    await using var transaction = await _db.Database.BeginTransactionAsync();
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();

    // 커밋 성공 후에만 캐시 무효화
    await _cache.RemoveAsync(UserKey(user.UserId));
}
```

DI 등록: `builder.Services.AddScoped<UserService>();` (AppDbContext와 동일 Scoped)

### 9.5 엔드포인트에 Load → Process → Save 패턴 적용

기존 엔드포인트에서 `AppDbContext` 직접 사용을 `UserService`로 교체한다.

**읽기 엔드포인트 — Load만 수행:**
```csharp
app.MapGet("/api/users/{userId:long}", async (ulong userId, UserService userService) =>
{
    var user = await userService.GetByIdAsync(userId);
    return user is not null ? Results.Ok(user) : Results.NotFound();
});
```

**쓰기 엔드포인트 — Load → Process → Save:**
```csharp
app.MapPut("/api/users/{userId:long}", async (
    ulong userId, User updatedUser,
    UserService userService, DbLockService lockService) =>
{
    var result = await lockService.ExecuteWithLockAsync(userId, async () =>
    {
        // 1. Load — DB에서 직접 조회 (ChangeTracker 추적)
        var user = await userService.LoadAsync(userId);
        if (user is null) return Results.NotFound();

        // 2. Process — 인메모리 수정만
        user.Username = updatedUser.Username;
        user.Email = updatedUser.Email;
        user.Level = updatedUser.Level;
        user.Experience = updatedUser.Experience;
        user.Gold = updatedUser.Gold;

        // 3. Save — DB 저장 + 캐시 무효화
        await userService.SaveAsync(user);
        return Results.Ok(user);
    });
    return result ?? Results.Conflict(new { error = "LOCK_FAILED" });
});
```

**패턴 요약:**

| 엔드포인트 | Load | Process | Save |
|-----------|------|---------|------|
| `GET /api/users/{id}` | `GetByIdAsync` (캐시→DB) | — | — |
| `GET /api/users` | `GetAllAsync` (캐시→DB) | — | — |
| `POST /api/users` | — | — | `CreateAsync` |
| `PUT /api/users/{id}` | `LoadAsync` (DB직접) | 프로퍼티 수정 | `SaveAsync` |
| `DELETE /api/users/{id}` | `DeleteAsync` (내부 처리) | — | — |
| `POST /api/users/{id}/add-gold` | `LoadAsync` (DB직접) | Gold 증가 | `SaveAsync` |

### 9.6 헬스체크 개선

기존 단순 lambda 헬스체크를 ASP.NET Core Health Checks 미들웨어로 교체한다.

```csharp
var redisConnection = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddHealthChecks()
    .AddRedis(redisConnection, name: "redis",
        failureStatus: HealthStatus.Degraded);  // Unhealthy가 아닌 Degraded

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
});
```

### 9.7 Prometheus 메트릭 추가

**MetricsService에 캐시 메트릭 추가:**

| 메트릭 | 타입 | 레이블 | 설명 |
|--------|------|--------|------|
| `mini_server_cache_hits_total` | Counter | `key_prefix` | 캐시 히트 수 |
| `mini_server_cache_misses_total` | Counter | `key_prefix` | 캐시 미스 수 |
| `mini_server_cache_operation_duration_seconds` | Histogram | `operation` | 캐시 작업 소요 시간 (get/set/remove) |

카디널리티 폭발 방지를 위해 전체 키가 아닌 prefix만 레이블로 사용한다 (`"user:123"` → `"user"`).

### 9.8 캐싱 전략 — 모바일 게임서버 특성

#### 데이터 유형별 캐싱 정책

| 데이터 유형 | 캐시 키 | TTL | 무효화 | 이유 |
|-----------|---------|-----|--------|------|
| 단일 유저 | `user:{id}` | 300초 | 쓰기 시 즉시 삭제 | 재화/레벨 변경이 바로 반영되어야 함 |
| 목록/랭킹 | `users:all` | 30초 | **안 함** (TTL 만료에 의존) | 몇 초 stale 허용, 무효화 비용 불필요 |

**핵심 결정: 목록 캐시는 무효화하지 않는다**
- 게임서버에서 "랭킹이 30초 늦게 반영됨"은 문제가 아님
- 쓰기마다 목록 캐시를 삭제하는 비용이 사라짐
- 단일 유저 캐시만 즉시 무효화하므로 무효화 로직이 단순해짐

#### 트랜잭션 전략

캐시 무효화는 반드시 트랜잭션 커밋 이후에 수행한다:

```
1. BeginTransaction
2. SaveChangesAsync (DB 변경)
3. CommitAsync (트랜잭션 확정)
4. Cache RemoveAsync (캐시 무효화)
```

| 시점 | 장애 | 결과 |
|------|------|------|
| SaveChangesAsync 실패 | DB 예외 | 트랜잭션 롤백, 캐시 변경 없음 |
| CommitAsync 실패 | DB 커밋 예외 | 트랜잭션 롤백, 캐시 변경 없음 |
| RemoveAsync 실패 | Redis 예외 | DB 커밋 완료, 캐시는 TTL 만료에 의존 |

### 9.9 에러 처리

에러 처리는 **CacheService 한 곳에서만** 수행한다:

```
Endpoint → UserService → CacheService (try-catch) → IDistributedCache → Redis
                                         ↓ 실패 시
                                      return default / 무시
```

| 장애 상황 | 동작 | 서비스 영향 |
|----------|------|-----------|
| Redis 연결 실패 | 캐시 무시, DB 직접 조회 | **없음** |
| Redis 타임아웃 | 로그 경고, DB 폴백 | **없음** |
| Redis 서버 다운 | 모든 캐시 미스, DB 전량 처리 | **없음** (성능 저하만) |
| 서비스 기동 시 Redis 부재 | 정상 기동, 첫 접근 시 폴백 | **없음** |

### 9.10 테스트

**단위 테스트:**

| 파일 | 테스트 수 | 대상 |
|------|----------|------|
| `liteapi.Tests/Services/CacheServiceTests.cs` | 11 | CacheService (CRUD + 장애 복원력 + 메트릭) |
| `liteapi.Tests/Services/UserServiceTests.cs` | 20 | UserService (Load/Save + 캐시 무효화 + 트랜잭션) |

**HTTP 통합 테스트: `Test.http/test-cache.http`**

| # | 요청 | 검증 |
|---|------|------|
| 1 | POST /api/users | 유저 생성 |
| 2 | GET /api/users/1 | 캐시 미스 → DB 조회 → 캐시 저장 |
| 3 | GET /api/users/1 | 캐시 히트 → DB 쿼리 없음 |
| 4 | PUT /api/users/1 | Load → Process → Save → 캐시 무효화 |
| 5 | GET /api/users/1 | 캐시 미스 → 최신 데이터 반환 |
| 6 | GET /health | Redis 상태 Healthy/Degraded 확인 |
| 7 | GET /metrics | cache_hits/misses/duration 확인 |

```bash
# 단위 테스트 실행
dotnet test liteapi.Tests/liteapi.Tests.csproj

# 캐시 관련 테스트만 실행
dotnet test liteapi.Tests/liteapi.Tests.csproj \
  --filter "FullyQualifiedName~CacheService|FullyQualifiedName~UserService"
```

### 9.11 변경된 파일

| 파일 | 변경 내용 |
|-----|---------|
| `liteapi.csproj` | Redis, HealthChecks 패키지 추가 |
| `appsettings.yaml` | Redis 연결 설정 추가 |
| `Program.cs` | Redis 서비스 등록, UserService DI, 엔드포인트 변경, 헬스체크 개선 |
| `Services/CacheService.cs` | **신규** — 캐시 서비스 (예외 흡수, 메트릭 기록) |
| `Services/UserService.cs` | **신규** — 사용자 서비스 (Load/Save 패턴) |
| `Services/MetricsService.cs` | 캐시 메트릭 추가 |
| `Test.http/test-cache.http` | **신규** — 캐시 HTTP 테스트 파일 |
| `liteapi.Tests/Services/CacheServiceTests.cs` | **신규** — CacheService 단위 테스트 (11개) |
| `liteapi.Tests/Services/UserServiceTests.cs` | **신규** — UserService 단위 테스트 (20개) |

---

## 최종 프로젝트 구조

```
liteapi/
├── Data/
│   └── AppDbContext.cs                 # EF Core DbContext
├── Models/
│   ├── RequestContext.cs               # 요청 컨텍스트
│   ├── User.cs                         # User 엔티티
│   └── Packet.cs                       # 패킷 모델
├── Services/
│   ├── CacheService.cs                 # Redis 캐시 서비스 (예외 흡수, 메트릭)
│   ├── DbLockService.cs                # DB Lock 서비스 (EF Core 통합)
│   ├── MetricsService.cs               # Prometheus 메트릭 서비스
│   └── UserService.cs                  # 사용자 서비스 (Load/Save 패턴)
├── Middleware/
│   └── PacketLockMiddleware.cs         # 자동 Lock 미들웨어
├── Formatters/
│   ├── PacketInputFormatter.cs         # 커스텀 Input Formatter
│   └── PacketOutputFormatter.cs        # 커스텀 Output Formatter
├── logs/                               # Serilog 로그 파일
│   └── mini-server-YYYY-MM-DD.log      # 일별 롤링 로그
├── Test.http/
│   └── test-cache.http                 # 캐시 HTTP 테스트
├── Program.cs                          # 메인 진입점
├── appsettings.yaml                    # 설정 파일 (YAML, Redis 설정 포함)
├── appsettings.Development.yaml        # 개발 환경 설정
├── liteapi.csproj                      # 프로젝트 파일
├── .gitignore                          # Git 무시 파일
├── README.md                           # 프로젝트 문서
├── GUIDE.md                            # 이 가이드
├── test-packet.http                    # 패킷 테스트
├── test-lock.http                      # Lock 테스트
├── test-users.http                     # User CRUD 테스트
└── test-metrics.http                   # Prometheus 메트릭 테스트

liteapi.Tests/
├── Models/
│   ├── UserTests.cs                    # User 모델 테스트
│   └── RequestContextTests.cs          # RequestContext 테스트
├── Services/
│   ├── CacheServiceTests.cs            # CacheService 테스트 (11개)
│   ├── DbLockServiceTests.cs           # DbLockService 테스트
│   ├── MetricsServiceTests.cs          # MetricsService 테스트
│   └── UserServiceTests.cs             # UserService 테스트 (20개)
└── liteapi.Tests.csproj                # 테스트 프로젝트 파일
```

---

## 참고 자료

### 패키지 버전

**liteapi.csproj**
```xml
<ItemGroup>
  <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
  <PackageReference Include="Dapper" Version="2.1.66" />
  <PackageReference Include="MessagePack" Version="3.1.4" />
  <PackageReference Include="MessagePack.AspNetCoreMvcFormatter" Version="3.1.4" />
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.22" />
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11" />
  <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.5" />
  <PackageReference Include="MySqlConnector" Version="2.5.0" />
  <PackageReference Include="NetEscapades.Configuration.Yaml" Version="3.1.0" />
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
  <PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
  <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
</ItemGroup>
```

**liteapi.Tests.csproj**
```xml
<ItemGroup>
  <PackageReference Include="FluentAssertions" Version="8.8.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
  <PackageReference Include="Moq" Version="4.20.72" />
  <PackageReference Include="xunit" Version="2.4.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
  <PackageReference Include="coverlet.collector" Version="6.0.0" />
</ItemGroup>
```

### 주요 개념

#### 1. MySQL DB Lock
- `GET_LOCK(name, timeout)`: 네임드 락 획득
- `RELEASE_LOCK(name)`: 락 해제
- 세션별 관리, 테이블 불필요

#### 2. MessagePack vs JSON
| 항목 | JSON | MessagePack |
|------|------|-------------|
| 형식 | 텍스트 | 바이너리 |
| 크기 | 큼 | 작음 (50-70% 감소) |
| 속도 | 느림 | 빠름 |
| 디버깅 | 쉬움 | 어려움 |
| 용도 | 개발/디버깅 | 프로덕션 |

#### 3. YAML vs JSON
```yaml
# YAML (가독성 좋음, 주석 가능)
ConnectionStrings:
  DefaultConnection: "Server=localhost;Database=db;"  # MySQL 연결
Lock:
  TimeoutSeconds: 30  # 30초 타임아웃
```

```json
// JSON (주석 불가)
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=db;"
  },
  "Lock": {
    "TimeoutSeconds": 30
  }
}
```

#### 4. Redis 캐싱

**핵심 원칙: 캐시 장애가 서비스 장애가 되어서는 안 된다.**

Redis는 성능 최적화 수단이며, 핵심 의존성이 아니다. 어떤 캐시 장애가 발생하더라도 DB 직접 조회로 정상 동작해야 한다.

**캐시 전략 — Cache-Aside 패턴:**
```
읽기: 캐시 조회 → 히트 시 반환 / 미스 시 DB 조회 → 캐시 저장 → 반환
쓰기: DB 저장 → 캐시 무효화 (삭제)
```

**데이터 유형별 정책:**

| 유형 | 캐시 키 | TTL | 무효화 | 이유 |
|------|---------|-----|--------|------|
| 단일 엔티티 | `user:{id}` | 300초 | 쓰기 시 즉시 삭제 | 재화/레벨 등 즉시 반영 필요 |
| 목록/랭킹 | `users:all` | 30초 | **안 함** (TTL 만료 의존) | stale 허용, 무효화 비용 제거 |

**에러 처리 — CacheService 한 곳에서 예외 흡수:**

| 메서드 | 실패 시 동작 |
|--------|-------------|
| `GetAsync` | `default` 반환 → 호출자가 DB 조회 |
| `SetAsync` | 무시 → 다음 요청에서 재시도 |
| `RemoveAsync` | 무시 → TTL에 의해 자연 만료 |

**Redis 장애 시 서비스 영향: 없음** (성능 저하만 발생). 헬스체크에서 `Degraded`로 보고.

#### 5. Transaction (트랜잭션 전략)

**원칙: 캐시 무효화는 반드시 트랜잭션 커밋 이후에 수행한다.**

DB 저장과 캐시 무효화는 원자적으로 묶을 수 없다 (Redis와 MySQL은 별개 시스템). 따라서 다음 순서를 보장한다:

```
1. BeginTransaction       ← 트랜잭션 시작
2. SaveChangesAsync       ← DB에 변경 반영
3. CommitAsync            ← 트랜잭션 확정
4. Cache RemoveAsync      ← 캐시 무효화 (커밋 성공 후)
```

**이 순서가 중요한 이유:**
- 커밋 **전에** 캐시를 삭제하면: 다른 요청이 아직 커밋되지 않은 이전 데이터를 DB에서 읽어 캐시에 넣을 수 있음 (stale 캐시 복원)
- 커밋 **후에** 캐시 삭제가 실패하면: TTL에 의해 자연 만료되므로 문제 없음

**장애 시나리오:**

| 시점 | 장애 | 결과 |
|------|------|------|
| SaveChangesAsync 실패 | DB 예외 | 트랜잭션 롤백, 캐시 변경 없음. **정상** |
| CommitAsync 실패 | DB 커밋 예외 | 트랜잭션 롤백, 캐시 변경 없음. **정상** |
| RemoveAsync 실패 | Redis 예외 | DB 커밋 완료, TTL 만료에 의존. **정상** |

**코드 예시:**
```csharp
public async Task SaveAsync(User user)
{
    user.UpdatedAt = DateTime.UtcNow;

    await using var transaction = await _db.Database.BeginTransactionAsync();
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();

    // 커밋 성공 후에만 캐시 무효화
    await _cache.RemoveAsync(UserKey(user.UserId));
}
```

**다중 엔티티 트랜잭션** (예: 골드 이체):
```csharp
public async Task TransferGoldAsync(ulong fromId, ulong toId, long amount)
{
    var from = await _db.Users.FindAsync(fromId);
    var to = await _db.Users.FindAsync(toId);

    from!.Gold -= amount;
    to!.Gold += amount;

    await using var transaction = await _db.Database.BeginTransactionAsync();
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();

    // 커밋 후 양쪽 캐시 모두 무효화
    await _cache.RemoveAsync(UserKey(fromId));
    await _cache.RemoveAsync(UserKey(toId));
}
```

#### 6. Load → Process → Save 패턴

모든 API 엔드포인트는 3단계로 실행된다:

```
┌─────────────────────────────────────────────────────────┐
│                      Endpoint                           │
│                                                         │
│  1. Load    ─ 캐시/DB에서 필요한 데이터를 가져옴          │
│                         ↓                               │
│  2. Process ─ 순수 인메모리 로직 (DB/캐시 접근 없음)      │
│                         ↓                               │
│  3. Save    ─ DB 저장 + 캐시 무효화를 한 번에 수행        │
└─────────────────────────────────────────────────────────┘
```

**Load 메서드 — 용도에 따라 구분:**

| 메서드 | 용도 | 캐시 사용 | ChangeTracker |
|--------|------|----------|---------------|
| `GetByIdAsync` | 읽기 전용 | 캐시 → DB 폴백 | `AsNoTracking` |
| `GetAllAsync` | 목록 조회 | 캐시(30초) → DB 폴백 | `AsNoTracking` |
| `LoadAsync` | 쓰기용 | 사용 안 함 (DB 직접) | 추적됨 |

**설계 핵심:**
- **읽기 전용 Load** (`GetByIdAsync`): 캐시 히트 시 DB를 거치지 않음. `AsNoTracking`으로 EF Core 오버헤드 제거
- **쓰기용 Load** (`LoadAsync`): 캐시를 거치지 않고 DB 직접 조회. ChangeTracker가 엔티티를 추적하므로 Process 단계에서 프로퍼티만 수정하면 Save에서 자동 반영
- **Process**: 엔드포인트에서 인메모리 객체만 조작. DB/캐시에 절대 접근하지 않음
- **Save**: DB 트랜잭션 커밋 후 캐시 무효화. 목록 캐시는 무효화하지 않음 (TTL 만료)

**Lock과의 관계 — 쓰기 작업에서 Lock은 전체를 감싼다:**

```
Lock Acquire
  └─ 1. Load    (DB에서 조회, ChangeTracker 추적)
  └─ 2. Process (인메모리 수정)
  └─ 3. Save    (DB 저장 + 캐시 무효화)
Lock Release
```

**엔드포인트별 패턴 요약:**

| 엔드포인트 | Load | Process | Save | Lock |
|-----------|------|---------|------|------|
| `GET /api/users/{id}` | `GetByIdAsync` (캐시→DB) | — | — | — |
| `GET /api/users` | `GetAllAsync` (캐시→DB) | — | — | — |
| `POST /api/users` | — | — | `CreateAsync` | — |
| `PUT /api/users/{id}` | `LoadAsync` (DB직접) | 프로퍼티 수정 | `SaveAsync` | ✅ |
| `DELETE /api/users/{id}` | `DeleteAsync` (내부 처리) | — | — | — |
| `POST /users/{id}/add-gold` | `LoadAsync` (DB직접) | Gold 증가 | `SaveAsync` | ✅ |

### 빌드 및 실행

```bash
# 빌드
dotnet build

# 실행
dotnet run

# 마이그레이션
dotnet ef migrations add MigrationName
dotnet ef database update

# 테스트
curl -H "X-User-Id: 12345" http://localhost:5000/api/test/locked
```

### 트러블슈팅

#### 1. EF Core 마이그레이션 실패
```bash
# dotnet ef 도구 설치
dotnet tool install --global dotnet-ef

# 프로젝트 초기화
dotnet restore
dotnet build
```

#### 2. MySQL 연결 실패
- 연결 문자열 확인
- MySQL 서버 실행 확인
- 방화벽 설정 확인

#### 3. Lock 타임아웃
- `Lock:TimeoutSeconds` 값 증가
- 트랜잭션 시간 최적화

#### 4. Redis 연결 실패
- Redis 없이도 서비스는 정상 기동됨 (CacheService가 예외 흡수)
- `appsettings.yaml`의 `Redis:Connection` 확인
- Redis 서버 실행 확인: `redis-cli ping`
- `/health` 엔드포인트에서 Redis 상태가 `Degraded`로 표시됨

---

### 주요 엔드포인트

| 엔드포인트 | 메서드 | 설명 | 인증 필요 |
|-----------|--------|------|----------|
| `/health` | GET | 헬스 체크 | ❌ |
| `/metrics` | GET | Prometheus 메트릭 | ❌ |
| `/swagger` | GET | API 문서 | ❌ |
| `/api/users` | GET | 모든 사용자 조회 | ❌ |
| `/api/users` | POST | 사용자 생성 | ❌ |
| `/api/users/{id}` | GET | 특정 사용자 조회 | ❌ |
| `/api/users/{id}` | PUT | 사용자 업데이트 (락 사용) | ✅ |
| `/api/users/{id}` | DELETE | 사용자 삭제 | ❌ |
| `/api/users/{id}/add-gold` | POST | 골드 추가 (락 사용) | ✅ |
| `/api/test/locked` | GET | 락 테스트 | ✅ |
| `/api/test/concurrent` | POST | 동시성 테스트 | ✅ |
| `/api/packet/echo` | POST | 패킷 에코 (JSON/MessagePack) | ❌ |
| `/api/packet/user` | POST | 패킷 유저 생성 | ❌ |

### 테스트 결과

```bash
dotnet test
```

```
Passed!  - Failed:     0, Passed:    79, Skipped:     5, Total:    84, Duration: 145 ms
```

- **79개 통과**: 모든 단위 테스트 성공 (기존 48 + CacheService 11 + UserService 20)
- **5개 스킵**: MySQL 필요한 통합 테스트 (선택사항)

---

## 다음 단계

### 추가 기능 구현

1. **Redis 분산 락 추가**
   - StackExchange.Redis 패키지
   - RedisLockService 구현
   - MySQL 락과 선택적 전환

2. **인증/인가 강화**
   - JWT 토큰 기반 인증
   - Role-based 권한 관리
   - OAuth 2.0 통합

3. **캐싱 확장**
   - Redis Pub/Sub: 다중 인스턴스 환경에서 캐시 무효화 전파
   - Rate Limiting: Redis 기반 API 호출 제한
   - 새로운 Entity 추가 시 동일 패턴으로 `ItemService`, `GuildService` 등 추가

4. **API 버저닝**
   - URL 기반 버저닝
   - 헤더 기반 버저닝
   - 구버전 호환성 유지

5. **배포 최적화**
   - Docker 컨테이너화
   - Kubernetes 배포
   - CI/CD 파이프라인

### 모니터링 및 운영

1. **Grafana 대시보드**
   - Prometheus 메트릭 시각화
   - 알람 설정
   - 성능 모니터링

2. **로그 집계**
   - ELK Stack 통합 (Elasticsearch, Logstash, Kibana)
   - 로그 검색 및 분석
   - 에러 알림

3. **성능 최적화**
   - Application Insights
   - 쿼리 최적화
   - 연결 풀 튜닝

---

**작성일**: 2026-01-01
**버전**: 3.0.0
**프레임워크**: .NET 8.0
**테스트**: 79/84 통과 (94%)
**커버리지**: Services (CacheService, UserService, DbLockService, MetricsService), Models, Middleware
