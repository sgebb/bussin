namespace ServiceBusExplorer.Blazor.Models;

public record ServiceBusTopicInfo
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long ScheduledMessageCount { get; init; }
    public long MaxSizeInMegabytes { get; init; }
    public long SizeInBytes { get; init; }
    public int SubscriptionCount { get; init; }
}
