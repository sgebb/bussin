using System.Text.Json;
using Bussin.Models;

namespace Bussin.Services;

public interface IMessageParsingService
{
    (object body, MessageProperties? properties, string? error) ParseMessageForSending(string messageBody, string? additionalProperties);
}

public sealed class MessageParsingService : IMessageParsingService
{
    public (object body, MessageProperties? properties, string? error) ParseMessageForSending(
        string messageBody, 
        string? additionalProperties)
    {
        if (string.IsNullOrWhiteSpace(messageBody))
        {
            return (null!, null, "Message body is required");
        }

        var properties = new MessageProperties();
        object bodyToSend;

        // Try to parse body as JSON
        if (messageBody.TrimStart().StartsWith("{") || messageBody.TrimStart().StartsWith("["))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(messageBody);
                bodyToSend = JsonSerializer.Deserialize<object>(messageBody)!;
                properties.ContentType = "application/json";
            }
            catch (JsonException)
            {
                // Not valid JSON, treat as plain text
                bodyToSend = messageBody;
                properties.ContentType = "text/plain";
            }
        }
        else
        {
            // Definitely not JSON, send as plain text
            bodyToSend = messageBody;
            properties.ContentType = "text/plain";
        }

        // Parse additional properties if provided
        if (!string.IsNullOrWhiteSpace(additionalProperties))
        {
            try
            {
                var additionalProps = JsonSerializer.Deserialize<Dictionary<string, object>>(additionalProperties);
                if (additionalProps != null)
                {
                    // Store as application properties
                    properties.ApplicationProperties = additionalProps;
                }
            }
            catch (JsonException ex)
            {
                return (null!, null, $"Invalid JSON in properties: {ex.Message}");
            }
        }

        return (bodyToSend, properties, null);
    }
}
