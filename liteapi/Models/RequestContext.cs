namespace liteapi.Models;

/// <summary>
/// Request context to hold user information for the current request
/// </summary>
public class RequestContext
{
    public ulong UserId { get; set; }
    public string? SessionToken { get; set; }

    public bool IsAuthenticated => UserId > 0;
}
