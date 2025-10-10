using Microsoft.JSInterop;
using System.Text.Json;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class PreferencesService(IJSRuntime jsRuntime) : IPreferencesService
{
    private const string PreferencesKey = "servicebus_preferences";

    public async Task<AppPreferences> LoadPreferencesAsync()
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("storageHelper.getItem", PreferencesKey);
            
            if (string.IsNullOrEmpty(json))
            {
                return new AppPreferences();
            }

            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading preferences: {ex.Message}");
            return new AppPreferences();
        }
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        try
        {
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
            await jsRuntime.InvokeVoidAsync("storageHelper.removeItem", PreferencesKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing preferences: {ex.Message}");
            throw;
        }
    }
}
