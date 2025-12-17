using System.Text.Json.Serialization;

namespace Bussin.Models;

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
    
    /// <summary>
    /// Time to live as TimeSpan - will be serialized as milliseconds
    /// </summary>
    [JsonIgnore]
    public TimeSpan? TimeToLive { get; set; }
    
    /// <summary>
    /// Time to live in milliseconds for JSON serialization
    /// </summary>
    [JsonPropertyName("time_to_live")]
    public long? TimeToLiveMs => TimeToLive.HasValue ? (long)TimeToLive.Value.TotalMilliseconds : null;
    
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
    /// Additional custom properties
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
