namespace ServiceBusExplorer.Blazor.Models;

public sealed class Folder
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<NamespaceConnection> Namespaces { get; set; } = new();
    public bool IsExpanded { get; set; } = false;
}
