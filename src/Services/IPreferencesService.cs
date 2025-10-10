using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public interface IPreferencesService
{
    Task<AppPreferences> LoadPreferencesAsync();
    Task SavePreferencesAsync(AppPreferences preferences);
    Task ClearPreferencesAsync();
}
