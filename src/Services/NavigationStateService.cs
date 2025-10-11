using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class NavigationStateService
{
    private readonly List<ServiceBusNamespaceInfo> _recentNamespaces = new();
    private const int MaxRecentNamespaces = 5;

    public event Action? OnChange;

    public IReadOnlyList<ServiceBusNamespaceInfo> RecentNamespaces => _recentNamespaces.AsReadOnly();

    public void AddRecentNamespace(ServiceBusNamespaceInfo namespaceInfo)
    {
        // Remove if already exists
        var existing = _recentNamespaces.FirstOrDefault(n => 
            n.FullyQualifiedNamespace == namespaceInfo.FullyQualifiedNamespace);
        
        if (existing != null)
        {
            _recentNamespaces.Remove(existing);
        }

        // Add to the beginning
        _recentNamespaces.Insert(0, namespaceInfo);

        // Keep only the most recent ones
        while (_recentNamespaces.Count > MaxRecentNamespaces)
        {
            _recentNamespaces.RemoveAt(_recentNamespaces.Count - 1);
        }

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
