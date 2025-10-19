namespace ServiceBusExplorer.Blazor.Models;

public sealed class AppPreferences
{
    public List<Folder> Folders { get; set; } = new();
    public bool DarkMode { get; set; } = false;
}
