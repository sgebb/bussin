using Microsoft.JSInterop;

namespace Bussin.Services;

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

}
