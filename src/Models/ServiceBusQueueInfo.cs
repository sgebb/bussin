namespace ServiceBusExplorer.Blazor.Models;

public class ServiceBusQueueInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long ActiveMessageCount { get; set; }
    public long DeadLetterMessageCount { get; set; }
    public long ScheduledMessageCount { get; set; }
    public long TransferMessageCount { get; set; }
    public long TransferDeadLetterMessageCount { get; set; }
    public long TotalMessageCount => ActiveMessageCount + DeadLetterMessageCount + ScheduledMessageCount + TransferMessageCount + TransferDeadLetterMessageCount;
    public long MaxSizeInMegabytes { get; set; }
    public long SizeInBytes { get; set; }
}
