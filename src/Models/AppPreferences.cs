namespace ServiceBusExplorer.Blazor.Models;

public sealed class AppPreferences
{
    public List<Folder> Folders { get; set; } = new();
    public bool DarkMode { get; set; } = false;
}

public sealed class Folder
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<NamespaceConnection> Namespaces { get; set; } = new();
    public bool IsExpanded { get; set; } = false;
}
