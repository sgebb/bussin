using Bussin.Models;
using System.Collections.Concurrent;

namespace Bussin.Services;

/// <summary>
/// Short-lived, immutable snapshots of Service Bus management resources.
/// A cache hit never exposes the list stored by the cache to callers.
/// </summary>
public sealed class ServiceBusEntityCache
{
    private static readonly TimeSpan NamespaceExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EntityExpiry = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusQueueInfo>> _queues = new();
    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusTopicInfo>> _topics = new();
    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusSubscriptionInfo>> _subscriptions = new();
    private CacheEntry<ServiceBusNamespaceInfo>? _namespaces;

    public List<ServiceBusNamespaceInfo>? GetNamespaces() => GetValidSnapshot(_namespaces, NamespaceExpiry);

    public void SetNamespaces(IEnumerable<ServiceBusNamespaceInfo> namespaces) =>
        _namespaces = new CacheEntry<ServiceBusNamespaceInfo>(namespaces.ToArray(), DateTimeOffset.UtcNow);

    public List<ServiceBusQueueInfo>? GetQueues(string namespaceKey) =>
        GetValidSnapshot(_queues.TryGetValue(namespaceKey, out var entry) ? entry : null, EntityExpiry);

    public void SetQueues(string namespaceKey, IEnumerable<ServiceBusQueueInfo> queues) =>
        _queues[namespaceKey] = new CacheEntry<ServiceBusQueueInfo>(queues.ToArray(), DateTimeOffset.UtcNow);

    public List<ServiceBusTopicInfo>? GetTopics(string namespaceKey) =>
        GetValidSnapshot(_topics.TryGetValue(namespaceKey, out var entry) ? entry : null, EntityExpiry);

    public void SetTopics(string namespaceKey, IEnumerable<ServiceBusTopicInfo> topics) =>
        _topics[namespaceKey] = new CacheEntry<ServiceBusTopicInfo>(topics.ToArray(), DateTimeOffset.UtcNow);

    public List<ServiceBusSubscriptionInfo>? GetSubscriptions(string subscriptionKey) =>
        GetValidSnapshot(_subscriptions.TryGetValue(subscriptionKey, out var entry) ? entry : null, EntityExpiry);

    public void SetSubscriptions(string subscriptionKey, IEnumerable<ServiceBusSubscriptionInfo> subscriptions) =>
        _subscriptions[subscriptionKey] = new CacheEntry<ServiceBusSubscriptionInfo>(subscriptions.ToArray(), DateTimeOffset.UtcNow);
    public void Clear()
    {
        _namespaces = null;
        _queues.Clear();
        _topics.Clear();
        _subscriptions.Clear();
    }

    private static List<T>? GetValidSnapshot<T>(CacheEntry<T>? entry, TimeSpan expiry)
    {
        if (entry is null || DateTimeOffset.UtcNow - entry.CreatedAt >= expiry)
            return null;

        return entry.Items.ToList();
    }

    private sealed record CacheEntry<T>(IReadOnlyList<T> Items, DateTimeOffset CreatedAt);
}
