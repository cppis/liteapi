using MessagePack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace liteapi.Formatters;

/// <summary>
/// Custom input formatter supporting both JSON and MessagePack
/// </summary>
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
                // MessagePack deserialization
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms);
                ms.Position = 0;

                var result = MessagePackSerializer.Deserialize(context.ModelType, ms, MessagePackSerializerOptions.Standard);
                return await InputFormatterResult.SuccessAsync(result);
            }
            else
            {
                // JSON deserialization
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
