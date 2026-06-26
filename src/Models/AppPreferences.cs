namespace Bussin.Models;

public sealed class AppPreferences
{
    public List<Folder> Folders { get; set; } = new();
    public bool DarkMode { get; set; } = false;
    public string? SelectedTenantId { get; set; }
    public bool RunWithoutLogin { get; set; } = false;
    // Key: "{namespace}|{entityPath}", Value: list of ApplicationProperty keys to show as columns
    public Dictionary<string, List<string>> EntityColumnPreferences { get; set; } = new();
}

public sealed class Folder
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<NamespaceConnection> Namespaces { get; set; } = new();
    public bool IsExpanded { get; set; } = false;
    public string? ParentId { get; set; } = null;
    public string? TenantId { get; set; }
}
