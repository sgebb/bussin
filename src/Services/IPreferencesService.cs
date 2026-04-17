using Bussin.Models;

namespace Bussin.Services;

public interface IPreferencesService
{
    Task<AppPreferences> LoadPreferencesAsync();
    Task SavePreferencesAsync(AppPreferences preferences);
    Task ClearPreferencesAsync();
}
