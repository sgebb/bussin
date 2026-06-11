using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bussin.Models;

/// <summary>
/// Represents a rule (filter and optional action) on a topic subscription.
/// </summary>
public class SubscriptionRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("filterType")]
    public string FilterType { get; set; } = "Sql"; // Sql, Correlation, True, False
    
    [JsonPropertyName("sqlExpression")]
    public string? SqlExpression { get; set; }
    
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
    
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }
    
    [JsonPropertyName("to")]
    public string? To { get; set; }
    
    [JsonPropertyName("replyTo")]
    public string? ReplyTo { get; set; }
    
    [JsonPropertyName("label")]
    public string? Label { get; set; }
    
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
    
    [JsonPropertyName("replyToSessionId")]
    public string? ReplyToSessionId { get; set; }
    
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }
    
    [JsonPropertyName("properties")]
    public System.Text.Json.JsonElement? Properties { get; set; }
    
    [JsonPropertyName("actionExpression")]
    public string? ActionExpression { get; set; }
}
