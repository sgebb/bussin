using Bussin.Models;
using System.Collections.Concurrent;

namespace Bussin.Services;

/// <summary>
/// Last known, immutable snapshots of Service Bus management resources, shown instantly while
/// every read fetches fresh data in the background, so they never expire.
/// A cache hit never exposes the list stored by the cache to callers.
/// </summary>
public sealed class ServiceBusEntityCache
{
    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusQueueInfo>> _queues = new();
    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusTopicInfo>> _topics = new();
    private readonly ConcurrentDictionary<string, CacheEntry<ServiceBusSubscriptionInfo>> _subscriptions = new();
    private CacheEntry<ServiceBusNamespaceInfo>? _namespaces;

    public List<ServiceBusNamespaceInfo>? GetNamespaces() => GetSnapshot(_namespaces);

    public void SetNamespaces(IEnumerable<ServiceBusNamespaceInfo> namespaces) =>
        _namespaces = new CacheEntry<ServiceBusNamespaceInfo>(namespaces.ToArray());

    public List<ServiceBusQueueInfo>? GetQueues(string namespaceKey) =>
        GetSnapshot(_queues.TryGetValue(namespaceKey, out var entry) ? entry : null);

    public void SetQueues(string namespaceKey, IEnumerable<ServiceBusQueueInfo> queues) =>
        _queues[namespaceKey] = new CacheEntry<ServiceBusQueueInfo>(queues.ToArray());

    public List<ServiceBusTopicInfo>? GetTopics(string namespaceKey) =>
        GetSnapshot(_topics.TryGetValue(namespaceKey, out var entry) ? entry : null);

    public void SetTopics(string namespaceKey, IEnumerable<ServiceBusTopicInfo> topics) =>
        _topics[namespaceKey] = new CacheEntry<ServiceBusTopicInfo>(topics.ToArray());

    public List<ServiceBusSubscriptionInfo>? GetSubscriptions(string subscriptionKey) =>
        GetSnapshot(_subscriptions.TryGetValue(subscriptionKey, out var entry) ? entry : null);

    public void SetSubscriptions(string subscriptionKey, IEnumerable<ServiceBusSubscriptionInfo> subscriptions) =>
        _subscriptions[subscriptionKey] = new CacheEntry<ServiceBusSubscriptionInfo>(subscriptions.ToArray());
    public void Clear()
    {
        _namespaces = null;
        _queues.Clear();
        _topics.Clear();
        _subscriptions.Clear();
    }

    private static List<T>? GetSnapshot<T>(CacheEntry<T>? entry) => entry?.Items.ToList();

    private sealed record CacheEntry<T>(IReadOnlyList<T> Items);
}
