using Microsoft.JSInterop;
using System.Text.Json;
using Bussin.Models;

namespace Bussin.Services;

public sealed class PreferencesService(IJSRuntime jsRuntime) : IPreferencesService
{
    private const string PreferencesKey = "servicebus_preferences";
    private AppPreferences? _cachedPreferences;

    public async Task<AppPreferences> LoadPreferencesAsync()
    {
        if (_cachedPreferences != null)
        {
            return _cachedPreferences;
        }

        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("storageHelper.getItem", PreferencesKey);
            
            if (string.IsNullOrEmpty(json))
            {
                _cachedPreferences = new AppPreferences();
            }
            else
            {
                _cachedPreferences = JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading preferences: {ex.Message}");
            _cachedPreferences = new AppPreferences();
        }

        return _cachedPreferences;
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        try
        {
            _cachedPreferences = preferences;
            var json = JsonSerializer.Serialize(preferences);
            await jsRuntime.InvokeVoidAsync("storageHelper.setItem", PreferencesKey, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving preferences: {ex.Message}");
            throw;
        }
    }

    public async Task ClearPreferencesAsync()
    {
        try
        {
            _cachedPreferences = null;
            await jsRuntime.InvokeVoidAsync("storageHelper.removeItem", PreferencesKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing preferences: {ex.Message}");
            throw;
        }
    }
}
