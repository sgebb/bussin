namespace ServiceBusExplorer.Blazor.Models;

public sealed class ServiceBusNamespaceInfo
{
    public required string Name { get; set; }
    public required string FullyQualifiedNamespace { get; set; }
    public required string ResourceGroup { get; set; }
    public required string SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }
    public string? TenantId { get; set; }
    public string? Location { get; set; }
}
