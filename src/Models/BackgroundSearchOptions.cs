namespace Bussin.Models;

/// <summary>
/// Options for a background message search operation
/// </summary>
public class BackgroundSearchOptions
{
    public required string NamespaceName { get; init; }
    public required string EntityType { get; init; } // "queue" or "subscription"
    public required string EntityPath { get; init; }
    public string? TopicName { get; init; } // Only for subscriptions
    public string? SubscriptionName { get; init; } // Only for subscriptions
    public required bool IsDeadLetter { get; init; }
    public long TotalMessageCount { get; init; }
    public int MaxMatches { get; init; } = 50;
    
    // Filter criteria
    public string? BodyFilter { get; init; }
    public string? MessageIdFilter { get; init; }
    public string? SubjectFilter { get; init; }
    
    public bool HasActiveFilter => 
        !string.IsNullOrWhiteSpace(BodyFilter) || 
        !string.IsNullOrWhiteSpace(MessageIdFilter) || 
        !string.IsNullOrWhiteSpace(SubjectFilter);
    
    public string GetFilterDescription()
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(MessageIdFilter))
            filters.Add($"ID: {MessageIdFilter}");
        if (!string.IsNullOrWhiteSpace(SubjectFilter))
            filters.Add($"Subject: {SubjectFilter}");
        if (!string.IsNullOrWhiteSpace(BodyFilter))
            filters.Add($"Body: {BodyFilter}");
        return string.Join(", ", filters);
    }
}
