namespace ServiceBusExplorer.Blazor.Models;

public sealed class NamespaceConnection
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required ConnectionType ConnectionType { get; set; }
    public required string ConnectionValue { get; set; }
    public bool IsDefault { get; set; }
}
