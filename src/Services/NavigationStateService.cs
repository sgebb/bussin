using Bussin.Models;

namespace Bussin.Services;

public sealed class NavigationStateService(IPreferencesService preferencesService)
{
    private const string DefaultFolderId = "namespaces";
    private const string DefaultFolderName = "Favorites";
    private AppPreferences _preferences = new();
    private bool _isInitialized = false;

    // Delete folder modal state
    private bool _showDeleteFolderModal = false;
    private string _folderIdToDelete = "";
    private string _folderNameToDelete = "";
    private int _folderNamespaceCount = 0;

    // Support modal state
    private bool _showSupportModal = false;

    public event Action? OnChange;
    public event Action? OnDeleteModalChange;
    public event Action? OnSupportModalChange;

    public IReadOnlyList<Folder> Folders => _preferences?.Folders?.AsReadOnly() ?? new List<Folder>().AsReadOnly();
    public bool IsInitialized => _isInitialized;

    // Delete folder modal properties
    public bool ShowDeleteFolderModal => _showDeleteFolderModal;
    public string FolderIdToDelete => _folderIdToDelete;
    public string FolderNameToDelete => _folderNameToDelete;
    public int FolderNamespaceCount => _folderNamespaceCount;

    // Support modal properties
    public bool ShowSupportModal => _showSupportModal;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        
        _preferences = await preferencesService.LoadPreferencesAsync();
        
        // Ensure default "Namespaces" folder exists
        if (!_preferences.Folders.Any(f => f.Id == DefaultFolderId))
        {
            _preferences.Folders.Insert(0, new Folder
            {
                Id = DefaultFolderId,
                Name = DefaultFolderName,
                IsExpanded = true
            });
            await preferencesService.SavePreferencesAsync(_preferences);
        }
        
        _isInitialized = true;
        NotifyStateChanged();
    }

    public bool IsFavorite(string fullyQualifiedNamespace)
    {
        return _preferences.Folders
            .SelectMany(f => f.Namespaces)
            .Any(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
    }

    public async Task AddToFavoritesAsync(string fullyQualifiedNamespace, string resourceGroup, string subscriptionId, string displayName)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
        
        var defaultFolder = _preferences.Folders.FirstOrDefault(f => f.Id == DefaultFolderId);
        if (defaultFolder == null)
        {
            defaultFolder = new Folder
            {
                Id = DefaultFolderId,
                Name = DefaultFolderName,
                IsExpanded = true
            };
            _preferences.Folders.Insert(0, defaultFolder);
        }

        // Check if already exists
        if (!defaultFolder.Namespaces.Any(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace))
        {
            defaultFolder.Namespaces.Add(new NamespaceConnection
            {
                FullyQualifiedNamespace = fullyQualifiedNamespace,
                ResourceGroup = resourceGroup,
                SubscriptionId = subscriptionId,
                DisplayName = displayName
            });

            await preferencesService.SavePreferencesAsync(_preferences);
            NotifyStateChanged();
        }
    }

    public async Task RemoveFromFavoritesAsync(string fullyQualifiedNamespace)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        
        foreach (var folder in _preferences.Folders)
        {
            var connection = folder.Namespaces.FirstOrDefault(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
            if (connection != null)
            {
                folder.Namespaces.Remove(connection);
                await preferencesService.SavePreferencesAsync(_preferences);
                NotifyStateChanged();
                return;
            }
        }
    }

    public async Task ToggleFolderExpandedAsync(string folderId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        
        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder != null)
        {
            folder.IsExpanded = !folder.IsExpanded;
            await preferencesService.SavePreferencesAsync(_preferences);
            NotifyStateChanged();
        }
    }

    public async Task CreateFolderAsync(string folderName)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        var newFolder = new Folder
        {
            Id = Guid.NewGuid().ToString(),
            Name = folderName,
            IsExpanded = true
        };

        _preferences.Folders.Add(newFolder);
        await preferencesService.SavePreferencesAsync(_preferences);
        NotifyStateChanged();
    }

    public async Task DeleteFolderAsync(string folderId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;

        var folderToDelete = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folderToDelete == null) return;

        // Don't allow deleting the default folder
        if (folderToDelete.Id == DefaultFolderId) return;

        // Move all namespaces from this folder to the default folder
        var defaultFolder = _preferences.Folders.FirstOrDefault(f => f.Id == DefaultFolderId);
        if (defaultFolder == null)
        {
            // Create default folder if it doesn't exist
            defaultFolder = new Folder
            {
                Id = DefaultFolderId,
                Name = DefaultFolderName,
                IsExpanded = true
            };
            _preferences.Folders.Add(defaultFolder);
        }

        // Move namespaces
        foreach (var ns in folderToDelete.Namespaces.ToList())
        {
            defaultFolder.Namespaces.Add(ns);
        }

        // Remove the folder
        _preferences.Folders.Remove(folderToDelete);
        
        await preferencesService.SavePreferencesAsync(_preferences);
        NotifyStateChanged();
    }

    public async Task RenameNamespaceAsync(string fullyQualifiedNamespace, string newDisplayName)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;

        foreach (var folder in _preferences.Folders)
        {
            var connection = folder.Namespaces.FirstOrDefault(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
            if (connection != null)
            {
                connection.DisplayName = newDisplayName;
                await preferencesService.SavePreferencesAsync(_preferences);
                NotifyStateChanged();
                return;
            }
        }
    }

    public async Task MoveNamespaceToFolderAsync(string fullyQualifiedNamespace, string targetFolderId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;

        NamespaceConnection? connectionToMove = null;
        Folder? sourceFolder = null;

        // Find the namespace and its current folder
        foreach (var folder in _preferences.Folders)
        {
            var connection = folder.Namespaces.FirstOrDefault(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
            if (connection != null)
            {
                connectionToMove = connection;
                sourceFolder = folder;
                break;
            }
        }

        if (connectionToMove == null || sourceFolder == null) return;

        // Find target folder
        var targetFolder = _preferences.Folders.FirstOrDefault(f => f.Id == targetFolderId);
        if (targetFolder == null) return;

        // Move the namespace
        sourceFolder.Namespaces.Remove(connectionToMove);
        targetFolder.Namespaces.Add(connectionToMove);

        await preferencesService.SavePreferencesAsync(_preferences);
        NotifyStateChanged();
    }

    public string? GetCurrentFolderId(string fullyQualifiedNamespace)
    {
        if (!_isInitialized || _preferences?.Folders == null) return null;

        foreach (var folder in _preferences.Folders)
        {
            if (folder.Namespaces.Any(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace))
            {
                return folder.Id;
            }
        }
        return null;
    }

    public NamespaceConnection? GetNamespaceConnection(string fullyQualifiedNamespace)
    {
        if (!_isInitialized || _preferences?.Folders == null) return null;

        return _preferences.Folders
            .SelectMany(f => f.Namespaces)
            .FirstOrDefault(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
    }

    public IEnumerable<Folder> GetChildFolders(string? parentId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return Enumerable.Empty<Folder>();
        return _preferences.Folders.Where(f => f.ParentId == parentId);
    }

    public IEnumerable<Folder> GetRootFolders()
    {
        return GetChildFolders(null);
    }

    public Folder? GetFolder(string folderId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return null;
        return _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
    }

    public async Task RenameFolderAsync(string folderId, string newName)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        if (folderId == DefaultFolderId) return;

        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder != null)
        {
            folder.Name = newName;
            await preferencesService.SavePreferencesAsync(_preferences);
            NotifyStateChanged();
        }
    }

    public async Task MoveFolderAsync(string folderId, string? targetParentId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        if (folderId == DefaultFolderId) return;
        if (folderId == targetParentId) return;

        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder == null) return;

        if (targetParentId != null && !_preferences.Folders.Any(f => f.Id == targetParentId))
            return;

        if (IsDescendantOf(targetParentId, folderId))
            return;

        folder.ParentId = targetParentId;
        await preferencesService.SavePreferencesAsync(_preferences);
        NotifyStateChanged();
    }

    private bool IsDescendantOf(string? potentialDescendantId, string ancestorId)
    {
        if (potentialDescendantId == null) return false;
        
        var current = _preferences.Folders.FirstOrDefault(f => f.Id == potentialDescendantId);
        while (current != null)
        {
            if (current.ParentId == ancestorId) return true;
            if (current.ParentId == null) return false;
            current = _preferences.Folders.FirstOrDefault(f => f.Id == current.ParentId);
        }
        return false;
    }

    public int GetFolderDepth(string folderId)
    {
        if (!_isInitialized || _preferences?.Folders == null) return 0;
        
        int depth = 0;
        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        while (folder?.ParentId != null)
        {
            depth++;
            folder = _preferences.Folders.FirstOrDefault(f => f.Id == folder.ParentId);
        }
        return depth;
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    // Delete folder modal methods
    public void ShowDeleteFolderConfirmation(string folderId, string folderName)
    {
        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder == null) return;

        _folderIdToDelete = folderId;
        _folderNameToDelete = folderName;
        _folderNamespaceCount = folder.Namespaces.Count;
        _showDeleteFolderModal = true;
        OnDeleteModalChange?.Invoke();
    }

    public void HideDeleteFolderModal()
    {
        _showDeleteFolderModal = false;
        _folderIdToDelete = "";
        _folderNameToDelete = "";
        _folderNamespaceCount = 0;
        OnDeleteModalChange?.Invoke();
    }

    public async Task ConfirmDeleteFolderAsync()
    {
        if (!string.IsNullOrEmpty(_folderIdToDelete))
        {
            await DeleteFolderAsync(_folderIdToDelete);
        }
        HideDeleteFolderModal();
    }

    // Support modal methods
    public void ShowSupportModalDialog()
    {
        _showSupportModal = true;
        OnSupportModalChange?.Invoke();
    }

    public void HideSupportModal()
    {
        _showSupportModal = false;
        OnSupportModalChange?.Invoke();
    }

    // Build info modal state
    private bool _showBuildModal = false;
    public bool ShowBuildModal => _showBuildModal;
    public event Action? OnBuildModalChange;

    public void ShowBuildModalDialog()
    {
        _showBuildModal = true;
        OnBuildModalChange?.Invoke();
    }

    public void HideBuildModal()
    {
        _showBuildModal = false;
        OnBuildModalChange?.Invoke();
    }
    public async Task ReorderFolderAsync(string folderId, int newIndex)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        if (folderId == DefaultFolderId) return; // Can't move default folder? Maybe we should allow it.
        // Let's assume default folder "Favorites" can be moved if user wants, or maybe kept at top?
        // User said "reorderable items".
        
        var folder = _preferences.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder != null)
        {
            var oldIndex = _preferences.Folders.IndexOf(folder);
            if (oldIndex > -1 && oldIndex != newIndex)
            {
                _preferences.Folders.RemoveAt(oldIndex);
                if (newIndex > _preferences.Folders.Count) newIndex = _preferences.Folders.Count;
                if (newIndex < 0) newIndex = 0;
                
                _preferences.Folders.Insert(newIndex, folder);
                await preferencesService.SavePreferencesAsync(_preferences);
                NotifyStateChanged();
            }
        }
    }

    public async Task ReorderNamespaceAsync(string fullyQualifiedNamespace, string targetFolderId, int newIndex)
    {
        if (!_isInitialized || _preferences?.Folders == null) return;
        
        // Find source
        NamespaceConnection? connectionToMove = null;
        Folder? sourceFolder = null;
        foreach (var f in _preferences.Folders)
        {
            var conn = f.Namespaces.FirstOrDefault(n => n.FullyQualifiedNamespace == fullyQualifiedNamespace);
            if (conn != null)
            {
                connectionToMove = conn;
                sourceFolder = f;
                break;
            }
        }

        if (connectionToMove == null || sourceFolder == null) return;

        var targetFolder = _preferences.Folders.FirstOrDefault(f => f.Id == targetFolderId);
        if (targetFolder == null) return;

        // If same folder, reorder
        if (sourceFolder == targetFolder)
        {
            var oldIndex = sourceFolder.Namespaces.IndexOf(connectionToMove);
            if (oldIndex > -1 && oldIndex != newIndex)
            {
                sourceFolder.Namespaces.RemoveAt(oldIndex);
                // Adjust index if we removed from before the insertion point
                // Actually, RemoveAt shifts indices. 
                // If moving down (old < new): insert at new-1?
                // Visual DnD usually calculates "insert before".
                // If I remove first, the list shrinks. 
                
                if (newIndex > sourceFolder.Namespaces.Count) newIndex = sourceFolder.Namespaces.Count;
                if (newIndex < 0) newIndex = 0;
                
                sourceFolder.Namespaces.Insert(newIndex, connectionToMove);
                await preferencesService.SavePreferencesAsync(_preferences);
                NotifyStateChanged();
            }
        }
        else
        {
            // Move to different folder at specific index
            sourceFolder.Namespaces.Remove(connectionToMove);
            
            if (newIndex > targetFolder.Namespaces.Count) newIndex = targetFolder.Namespaces.Count;
            if (newIndex < 0) newIndex = 0;
            
            targetFolder.Namespaces.Insert(newIndex, connectionToMove);
            await preferencesService.SavePreferencesAsync(_preferences);
            NotifyStateChanged();
        }
    }
}
