using MessagePack;

namespace liteapi.Models;

/// <summary>
/// Base packet class for request/response
/// </summary>
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

/// <summary>
/// Request packet for testing
/// </summary>
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

/// <summary>
/// Response packet for testing
/// </summary>
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

/// <summary>
/// User packet for CRUD operations
/// </summary>
[MessagePackObject]
public class UserPacket
{
    [Key(0)]
    public ulong UserId { get; set; }

    [Key(1)]
    public string Username { get; set; } = string.Empty;

    [Key(2)]
    public string? Email { get; set; }

    [Key(3)]
    public int Level { get; set; }

    [Key(4)]
    public long Gold { get; set; }
}
