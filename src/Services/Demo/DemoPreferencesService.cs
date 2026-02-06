using Bussin.Models;

namespace Bussin.Services.Demo;

/// <summary>
/// Demo implementation of IPreferencesService that stores preferences in memory
/// with demo-specific defaults, isolated from real user preferences.
/// </summary>
public sealed class DemoPreferencesService : IPreferencesService
{
    private AppPreferences _preferences;
    
    public DemoPreferencesService()
    {
        // Initialize with demo-specific defaults
        _preferences = CreateDemoPreferences();
    }
    
    private static AppPreferences CreateDemoPreferences()
    {
        return new AppPreferences
        {
            DarkMode = true, // Demo uses dark mode by default
            Folders = []
        };
    }
    
    public Task<AppPreferences> LoadPreferencesAsync()
    {
        return Task.FromResult(_preferences);
    }
    
    public Task SavePreferencesAsync(AppPreferences preferences)
    {
        // Store in memory only - changes persist for the session but are lost on refresh
        _preferences = preferences;
        return Task.CompletedTask;
    }
    
    public Task ClearPreferencesAsync()
    {
        // Reset to demo defaults instead of clearing completely
        _preferences = CreateDemoPreferences();
        return Task.CompletedTask;
    }
}
