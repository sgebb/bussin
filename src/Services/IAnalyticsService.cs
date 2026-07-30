namespace Bussin.Services;

public interface IAnalyticsService
{
    Task TrackPageViewAsync(string url, string title);
    Task TrackEventAsync(string eventName, Dictionary<string, object>? parameters = null);
}
