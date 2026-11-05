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

    /// <summary>The failures grouped by error, e.g. "Not found: #12, #13"; null when nothing failed.</summary>
    public string? FailureSummary() => Errors.Count == 0
        ? null
        : string.Join("; ", Errors.GroupBy(e => e.Error).Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.MessageId))}"));
}

public class BatchOperationError
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Set when the failed message is known by sequence number.</summary>
    [JsonPropertyName("sequenceNumber")]
    public long? SequenceNumber { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
