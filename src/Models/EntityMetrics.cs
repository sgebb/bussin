namespace Bussin.Models;

/// <summary>
/// Statistics for a Service Bus entity retrieved from Azure Monitor metrics.
/// </summary>
public record EntityMetrics
{
    /// <summary>
    /// Number of incoming messages in the time period.
    /// </summary>
    public long IncomingMessages { get; init; }
    
    /// <summary>
    /// Number of outgoing (completed) messages in the time period.
    /// </summary>
    public long OutgoingMessages { get; init; }
    
    /// <summary>
    /// Current size of the entity in bytes.
    /// </summary>
    public long SizeInBytes { get; init; }
    
    /// <summary>
    /// Number of active messages.
    /// </summary>
    public long ActiveMessages { get; init; }
    
    /// <summary>
    /// Number of dead-lettered messages.
    /// </summary>
    public long DeadLetteredMessages { get; init; }
    
    /// <summary>
    /// Number of scheduled messages.
    /// </summary>
    public long ScheduledMessages { get; init; }
    
    /// <summary>
    /// The time period start for the metrics (UTC).
    /// </summary>
    public DateTime PeriodStart { get; init; }
    
    /// <summary>
    /// The time period end for the metrics (UTC).
    /// </summary>
    public DateTime PeriodEnd { get; init; }
    
    /// <summary>
    /// Indicates if metrics were successfully retrieved.
    /// </summary>
    public bool IsAvailable { get; init; }
    
    /// <summary>
    /// Error message if metrics retrieval failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
