using Bussin.Models;
using System.Collections.Concurrent;

namespace Bussin.Services;

public class ServiceBusEntityCache
{
    private readonly ConcurrentDictionary<string, List<ServiceBusQueueInfo>> _queueCache = new();
    private readonly ConcurrentDictionary<string, List<ServiceBusTopicInfo>> _topicCache = new();
    private readonly ConcurrentDictionary<string, List<ServiceBusSubscriptionInfo>> _subscriptionCache = new();
    private readonly ConcurrentBag<ServiceBusNamespaceInfo> _namespacesCache = new();
    private DateTime? _namespacesCacheTime;
    private readonly TimeSpan _namespacesCacheExpiry = TimeSpan.FromMinutes(5);

    public List<ServiceBusNamespaceInfo>? GetNamespaces()
    {
        if (_namespacesCacheTime.HasValue)
        {
            if (DateTime.UtcNow - _namespacesCacheTime.Value < _namespacesCacheExpiry)
            {
                return _namespacesCache.ToList();
            }
        }
        return null;
    }

    public void SetNamespaces(List<ServiceBusNamespaceInfo> namespaces)
    {
        _namespacesCache.Clear();
        foreach (var ns in namespaces)
        {
            _namespacesCache.Add(ns);
        }
        _namespacesCacheTime = DateTime.UtcNow;
    }

    public List<ServiceBusQueueInfo>? GetQueues(string namespaceKey)
    {
        _queueCache.TryGetValue(namespaceKey, out var queues);
        return queues;
    }

    public void SetQueues(string namespaceKey, List<ServiceBusQueueInfo> queues)
    {
        _queueCache[namespaceKey] = queues;
    }

    public List<ServiceBusTopicInfo>? GetTopics(string namespaceKey)
    {
        _topicCache.TryGetValue(namespaceKey, out var topics);
        return topics;
    }

    public void SetTopics(string namespaceKey, List<ServiceBusTopicInfo> topics)
    {
        _topicCache[namespaceKey] = topics;
    }

    public List<ServiceBusSubscriptionInfo>? GetSubscriptions(string subscriptionKey)
    {
        _subscriptionCache.TryGetValue(subscriptionKey, out var subscriptions);
        return subscriptions;
    }

    public void SetSubscriptions(string subscriptionKey, List<ServiceBusSubscriptionInfo> subscriptions)
    {
        _subscriptionCache[subscriptionKey] = subscriptions;
    }
    public void Clear()
    {
        _namespacesCacheTime = null;
        _namespacesCache.Clear();
        _queueCache.Clear();
        _topicCache.Clear();
        _subscriptionCache.Clear();
    }
}
