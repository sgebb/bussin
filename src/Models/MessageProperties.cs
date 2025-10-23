using System.Text.Json.Serialization;

namespace ServiceBusExplorer.Blazor.Models;

/// <summary>
/// Properties for sending Service Bus messages
/// Supports both snake_case and camelCase property names for compatibility
/// </summary>
public class MessageProperties
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }
    
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
    
    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; set; }
    
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
    
    [JsonPropertyName("to")]
    public string? To { get; set; }
    
    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; set; }
    
    [JsonPropertyName("reply_to_session_id")]
    public string? ReplyToSessionId { get; set; }
    
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
    
    [JsonPropertyName("partition_key")]
    public string? PartitionKey { get; set; }
    
    [JsonPropertyName("time_to_live")]
    public int? TimeToLive { get; set; }
    
    /// <summary>
    /// Message annotations - used for scheduled messages and other advanced scenarios
    /// </summary>
    [JsonPropertyName("message_annotations")]
    public Dictionary<string, object>? MessageAnnotations { get; set; }
    
    /// <summary>
    /// Application-specific properties
    /// </summary>
    [JsonPropertyName("application_properties")]
    public Dictionary<string, object>? ApplicationProperties { get; set; }
    
    /// <summary>
    /// Original binary body - when provided, this preserves the exact message format for resend operations
    /// Takes precedence over other body properties to avoid format corruption
    /// </summary>
    [JsonPropertyName("original_body")]
    public object? OriginalBody { get; set; }

    /// <summary>
    /// Original content type - used with original_body to preserve exact message format
    /// </summary>
    [JsonPropertyName("original_content_type")]
    public string? OriginalContentType { get; set; }

    /// <summary>
    /// Additional custom properties
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
