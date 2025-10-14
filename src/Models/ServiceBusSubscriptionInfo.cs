namespace ServiceBusExplorer.Blazor.Models;

public class ServiceBusSubscriptionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long ActiveMessageCount { get; set; }
    public long DeadLetterMessageCount { get; set; }
    public long TransferMessageCount { get; set; }
    public long TransferDeadLetterMessageCount { get; set; }
    public long TotalMessageCount => ActiveMessageCount + DeadLetterMessageCount + TransferMessageCount + TransferDeadLetterMessageCount;
}
