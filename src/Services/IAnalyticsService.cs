namespace Bussin.Services;

public interface IAnalyticsService
{
    Task TrackPageViewAsync(string url, string title);
    Task TrackEventAsync(string eventName, Dictionary<string, object>? parameters = null);
    Task TrackExceptionAsync(string description, bool fatal = false);
    Task TrackTimingAsync(string category, string variable, int value, string? label = null);
}
