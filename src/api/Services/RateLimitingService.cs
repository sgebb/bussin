using System;
using System.Collections.Concurrent;

namespace Bussin.Backend.Services;

public interface IRateLimitingService
{
    bool IsAllowed(string ipAddress, int limitPerMinute = 15);
}

public class RateLimitingService : IRateLimitingService
{
    private class RateLimitTracker
    {
        public int Count { get; set; }
        public DateTime MinuteStart { get; set; }
    }

    private readonly ConcurrentDictionary<string, RateLimitTracker> _trackers = new();

    public bool IsAllowed(string ipAddress, int limitPerMinute = 15)
    {
        if (string.IsNullOrEmpty(ipAddress)) return true; // Fail-open if no IP parsed

        var now = DateTime.UtcNow;
        var tracker = _trackers.AddOrUpdate(ipAddress,
            _ => new RateLimitTracker { Count = 1, MinuteStart = now },
            (_, existing) =>
            {
                if ((now - existing.MinuteStart).TotalMinutes >= 1)
                {
                    existing.Count = 1;
                    existing.MinuteStart = now;
                }
                else
                {
                    existing.Count++;
                }
                return existing;
            });

        return tracker.Count <= limitPerMinute;
    }
}
