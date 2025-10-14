namespace ServiceBusExplorer.Blazor.Models;

public class ServiceBusTopicInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long ScheduledMessageCount { get; set; }
    public long MaxSizeInMegabytes { get; set; }
    public long SizeInBytes { get; set; }
    public int SubscriptionCount { get; set; }
}
