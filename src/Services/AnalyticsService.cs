using Microsoft.JSInterop;

namespace ServiceBusExplorer.Blazor.Services;

public class AnalyticsService : IAnalyticsService
{
    private readonly IJSRuntime _jsRuntime;

    public AnalyticsService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task TrackPageViewAsync(string url, string title)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("Analytics.trackPageView", url, title);
        }
        catch
        {
            // Silently fail if analytics is not available
        }
    }

    public async Task TrackEventAsync(string eventName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("Analytics.trackEvent", eventName, parameters ?? new Dictionary<string, object>());
        }
        catch
        {
            // Silently fail if analytics is not available
        }
    }

    public async Task TrackExceptionAsync(string description, bool fatal = false)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("Analytics.trackException", description, fatal);
        }
        catch
        {
            // Silently fail if analytics is not available
        }
    }

    public async Task TrackTimingAsync(string category, string variable, int value, string? label = null)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("Analytics.trackTiming", category, variable, value, label);
        }
        catch
        {
            // Silently fail if analytics is not available
        }
    }
}
