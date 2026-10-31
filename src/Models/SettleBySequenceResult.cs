using System.Text.Json.Serialization;

namespace Bussin.Models;

/// <summary>
/// Outcome of deleting or dead-lettering messages by sequence number.
/// </summary>
public class SettleBySequenceResult
{
    /// <summary>Sequence numbers that were completed or dead-lettered.</summary>
    [JsonPropertyName("settled")]
    public long[] Settled { get; set; } = [];

    /// <summary>Requested sequence numbers that were not found among the locked messages.</summary>
    [JsonPropertyName("notFound")]
    public long[] NotFound { get; set; } = [];

    public BatchOperationResult ToBatchResult() => new()
    {
        SuccessCount = Settled.Length,
        FailureCount = NotFound.Length,
        Errors = NotFound.Select(seq => new BatchOperationError
        {
            MessageId = $"#{seq}",
            Error = "Not found: it may already be gone, or be locked by another receiver"
        }).ToList()
    };
}
