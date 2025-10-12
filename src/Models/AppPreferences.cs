namespace ServiceBusExplorer.Blazor.Models;

public sealed class AppPreferences
{
    public int Version { get; set; } = 2;
    public List<Folder> Folders { get; set; } = new();
    public bool DarkMode { get; set; } = false;
}
