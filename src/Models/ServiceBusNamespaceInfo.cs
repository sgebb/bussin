namespace ServiceBusExplorer.Blazor.Models;

public sealed record ServiceBusNamespaceInfo
{
    public required string Name { get; init; }
    public required string FullyQualifiedNamespace { get; init; }
    public required string ResourceGroup { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionName { get; init; }
    public string? TenantId { get; init; }
    public string? Location { get; init; }
}
