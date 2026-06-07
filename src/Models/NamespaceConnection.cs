namespace Bussin.Models;

public sealed class NamespaceConnection
{
    public required string FullyQualifiedNamespace { get; set; }
    public required string ResourceGroup { get; set; }
    public required string SubscriptionId { get; set; }
    
    // Display name - can be customized by user, defaults to namespace name without .servicebus.windows.net
    public required string DisplayName { get; set; }
    
    // Connection string details
    public string? ConnectionString { get; set; }
    public string? WebSocketUrl { get; set; }
    public List<string> ConfiguredQueues { get; set; } = new();
    public List<ConfiguredTopic> ConfiguredTopics { get; set; } = new();
}

public sealed class ConfiguredTopic
{
    public required string TopicName { get; set; }
    public List<string> Subscriptions { get; set; } = new();
}
