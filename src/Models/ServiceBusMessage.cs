using System.Text.Json.Serialization;

namespace ServiceBusExplorer.Blazor.Models;

public class ServiceBusMessage
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }
    
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
    
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
    
    [JsonPropertyName("deliveryCount")]
    public int DeliveryCount { get; set; }
    
    [JsonPropertyName("enqueuedTime")]
    public DateTime? EnqueuedTime { get; set; }
    
    [JsonPropertyName("sequenceNumber")]
    public long? SequenceNumber { get; set; }
    
    [JsonPropertyName("lockedUntil")]
    public DateTime? LockedUntil { get; set; }
    
    [JsonPropertyName("applicationProperties")]
    public Dictionary<string, object>? ApplicationProperties { get; set; }
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object>? Properties { get; set; }
    
    [JsonPropertyName("ttl")]
    public long? Ttl { get; set; }
    
    [JsonPropertyName("expiryTime")]
    public DateTime? ExpiryTime { get; set; }
    
    [JsonPropertyName("creationTime")]
    public DateTime? CreationTime { get; set; }
    
    [JsonPropertyName("originalBody")]
    public object? OriginalBody { get; set; }

    [JsonPropertyName("originalContentType")]
    public string? OriginalContentType { get; set; }

    [JsonPropertyName("lockToken")]
    public string? LockToken { get; set; }
}
