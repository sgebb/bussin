namespace Bussin.Models;

public record ServiceBusQueueInfo
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long ActiveMessageCount { get; init; }
    public long DeadLetterMessageCount { get; init; }
    public long ScheduledMessageCount { get; init; }
    public long TransferMessageCount { get; init; }
    public long TransferDeadLetterMessageCount { get; init; }
    public long TotalMessageCount => ActiveMessageCount + DeadLetterMessageCount + ScheduledMessageCount + TransferMessageCount + TransferDeadLetterMessageCount;
    public long MaxSizeInMegabytes { get; init; }
    public long SizeInBytes { get; init; }
    
    // Entity properties
    public bool RequiresSession { get; init; }
    public bool EnablePartitioning { get; init; }
    public bool RequiresDuplicateDetection { get; init; }
    public bool DeadLetteringOnMessageExpiration { get; init; }
    public string? ForwardTo { get; init; }
    public string? ForwardDeadLetteredMessagesTo { get; init; }
}
