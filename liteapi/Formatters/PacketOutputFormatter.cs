using MessagePack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace liteapi.Formatters;

/// <summary>
/// Custom output formatter supporting both JSON and MessagePack
/// </summary>
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

        // Determine serialization format based on Accept header
        var useMessagePack = accept.Contains(MessagePackContentType, StringComparison.OrdinalIgnoreCase);

        if (useMessagePack)
        {
            // MessagePack serialization
            response.ContentType = MessagePackContentType;
            var bytes = MessagePackSerializer.Serialize(context.Object, MessagePackSerializerOptions.Standard);
            await response.Body.WriteAsync(bytes);
        }
        else
        {
            // JSON serialization (default)
            response.ContentType = JsonContentType;
            var json = JsonSerializer.Serialize(context.Object, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            await response.Body.WriteAsync(bytes);
        }
    }
}
