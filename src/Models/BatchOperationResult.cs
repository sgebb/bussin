using System.Text.Json.Serialization;

namespace Bussin.Models;

public class BatchOperationResult
{
    [JsonPropertyName("successCount")]
    public int SuccessCount { get; set; }
    
    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }
    
    [JsonPropertyName("errors")]
    public List<BatchOperationError> Errors { get; set; } = new();
}

public class BatchOperationError
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;
    
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
