namespace Bussin.Models;

public sealed class NamespaceConnection
{
    public required string FullyQualifiedNamespace { get; set; }
    public required string ResourceGroup { get; set; }
    public required string SubscriptionId { get; set; }
    
    // Display name - can be customized by user, defaults to namespace name without .servicebus.windows.net
    public required string DisplayName { get; set; }
}
