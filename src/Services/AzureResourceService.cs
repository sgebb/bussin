using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ServiceBus;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class AzureResourceService : IAzureResourceService
{
    private readonly ServiceBusEntityCache _cache;

    public AzureResourceService(ServiceBusEntityCache cache)
    {
        _cache = cache;
    }
    
    private string GetCacheKey(ServiceBusNamespaceInfo namespaceInfo)
    {
        return $"{namespaceInfo.SubscriptionId}/{namespaceInfo.ResourceGroup}/{namespaceInfo.Name}";
    }
    
    private string GetSubscriptionCacheKey(ServiceBusNamespaceInfo namespaceInfo, string topicName)
    {
        return $"{GetCacheKey(namespaceInfo)}/{topicName}";
    }

    private async IAsyncEnumerable<T> CachedStreamAsync<T>(
        List<T>? cached,
        Func<CancellationToken, IAsyncEnumerable<T>> fetchFresh,
        Action<List<T>> updateCache,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        // Yield cached items first for instant display
        if (cached != null && cached.Count > 0)
        {
            foreach (var item in cached)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return item;
            }
        }

        // Fetch fresh items - these will replace the cache
        var freshItems = new List<T>();
        
        await foreach (var item in fetchFresh(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) 
            {
                // Update cache with partial fresh results if cancelled
                if (freshItems.Count > 0)
                {
                    updateCache(freshItems);
                }
                yield break;
            }
            
            freshItems.Add(item);
            yield return item;
        }

        // Update cache with complete fresh list (replaces old cache)
        updateCache(freshItems);
    }

    public async IAsyncEnumerable<ServiceBusNamespaceInfo> ListServiceBusNamespacesAsync(
        TokenCredential credential, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var ns in CachedStreamAsync(
            _cache.GetNamespaces(),
            ct => FetchNamespacesAsync(credential, ct),
            fresh => _cache.SetNamespaces(fresh),
            cancellationToken))
        {
            yield return ns;
        }
    }

    private async IAsyncEnumerable<ServiceBusNamespaceInfo> FetchNamespacesAsync(
        TokenCredential credential,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var armClient = new ArmClient(credential);

        await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            
            await foreach (var serviceBusNamespace in subscription.GetServiceBusNamespacesAsync(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                
                yield return new ServiceBusNamespaceInfo
                {
                    Name = serviceBusNamespace.Data.Name,
                    FullyQualifiedNamespace = $"{serviceBusNamespace.Data.Name}.servicebus.windows.net",
                    ResourceGroup = serviceBusNamespace.Id.ResourceGroupName ?? "",
                    SubscriptionId = subscription.Data.SubscriptionId ?? "",
                    SubscriptionName = subscription.Data.DisplayName ?? "",
                    Location = serviceBusNamespace.Data.Location.Name,
                    TenantId = subscription.Data.TenantId?.ToString() ?? ""
                };
            }
        }
    }

    public async IAsyncEnumerable<ServiceBusQueueInfo> ListQueuesAsync(
        TokenCredential credential, 
        ServiceBusNamespaceInfo namespaceInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(namespaceInfo);
        
        await foreach (var queue in CachedStreamAsync(
            _cache.GetQueues(cacheKey),
            ct => FetchQueuesAsync(credential, namespaceInfo, ct),
            fresh => _cache.SetQueues(cacheKey, fresh),
            cancellationToken))
        {
            yield return queue;
        }
    }

    private async IAsyncEnumerable<ServiceBusQueueInfo> FetchQueuesAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var serviceBusNamespace = await GetServiceBusNamespaceResourceAsync(credential, namespaceInfo);

        await foreach (var queue in serviceBusNamespace.GetServiceBusQueues().GetAllAsync())
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            
            yield return new ServiceBusQueueInfo
            {
                Name = queue.Data.Name,
                Status = queue.Data.Status?.ToString() ?? "Unknown",
                ActiveMessageCount = queue.Data.CountDetails?.ActiveMessageCount ?? 0,
                DeadLetterMessageCount = queue.Data.CountDetails?.DeadLetterMessageCount ?? 0,
                ScheduledMessageCount = queue.Data.CountDetails?.ScheduledMessageCount ?? 0,
                TransferMessageCount = queue.Data.CountDetails?.TransferMessageCount ?? 0,
                TransferDeadLetterMessageCount = queue.Data.CountDetails?.TransferDeadLetterMessageCount ?? 0,
                MaxSizeInMegabytes = queue.Data.MaxSizeInMegabytes ?? 0,
                SizeInBytes = queue.Data.SizeInBytes ?? 0,
                RequiresSession = queue.Data.RequiresSession ?? false,
                EnablePartitioning = queue.Data.EnablePartitioning ?? false,
                RequiresDuplicateDetection = queue.Data.RequiresDuplicateDetection ?? false,
                DeadLetteringOnMessageExpiration = queue.Data.DeadLetteringOnMessageExpiration ?? false,
                ForwardTo = queue.Data.ForwardTo,
                ForwardDeadLetteredMessagesTo = queue.Data.ForwardDeadLetteredMessagesTo
            };
        }
    }

    public async IAsyncEnumerable<ServiceBusTopicInfo> ListTopicsAsync(
        TokenCredential credential, 
        ServiceBusNamespaceInfo namespaceInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(namespaceInfo);
        
        await foreach (var topic in CachedStreamAsync(
            _cache.GetTopics(cacheKey),
            ct => FetchTopicsAsync(credential, namespaceInfo, ct),
            fresh => _cache.SetTopics(cacheKey, fresh),
            cancellationToken))
        {
            yield return topic;
        }
    }

    private async IAsyncEnumerable<ServiceBusTopicInfo> FetchTopicsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var serviceBusNamespace = await GetServiceBusNamespaceResourceAsync(credential, namespaceInfo);

        await foreach (var topic in serviceBusNamespace.GetServiceBusTopics().GetAllAsync())
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            
            yield return new ServiceBusTopicInfo
            {
                Name = topic.Data.Name,
                Status = topic.Data.Status?.ToString() ?? "Unknown",
                ScheduledMessageCount = topic.Data.CountDetails?.ScheduledMessageCount ?? 0,
                MaxSizeInMegabytes = topic.Data.MaxSizeInMegabytes ?? 0,
                SizeInBytes = topic.Data.SizeInBytes ?? 0,
                SubscriptionCount = topic.Data.SubscriptionCount ?? 0
            };
        }
    }

    public async IAsyncEnumerable<ServiceBusSubscriptionInfo> ListSubscriptionsAsync(
        TokenCredential credential, 
        ServiceBusNamespaceInfo namespaceInfo, 
        string topicName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetSubscriptionCacheKey(namespaceInfo, topicName);
        
        await foreach (var sub in CachedStreamAsync(
            _cache.GetSubscriptions(cacheKey),
            ct => FetchSubscriptionsAsync(credential, namespaceInfo, topicName, ct),
            fresh => _cache.SetSubscriptions(cacheKey, fresh),
            cancellationToken))
        {
            yield return sub;
        }
    }

    private async IAsyncEnumerable<ServiceBusSubscriptionInfo> FetchSubscriptionsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string topicName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var serviceBusNamespace = await GetServiceBusNamespaceResourceAsync(credential, namespaceInfo);
        var topic = await serviceBusNamespace.GetServiceBusTopicAsync(topicName);

        await foreach (var sub in topic.Value.GetServiceBusSubscriptions().GetAllAsync())
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            
            yield return new ServiceBusSubscriptionInfo
            {
                Name = sub.Data.Name,
                Status = sub.Data.Status?.ToString() ?? "Unknown",
                ActiveMessageCount = sub.Data.CountDetails?.ActiveMessageCount ?? 0,
                DeadLetterMessageCount = sub.Data.CountDetails?.DeadLetterMessageCount ?? 0,
                TransferMessageCount = sub.Data.CountDetails?.TransferMessageCount ?? 0,
                TransferDeadLetterMessageCount = sub.Data.CountDetails?.TransferDeadLetterMessageCount ?? 0,
                RequiresSession = sub.Data.RequiresSession ?? false,
                DeadLetteringOnMessageExpiration = sub.Data.DeadLetteringOnMessageExpiration ?? false,
                ForwardTo = sub.Data.ForwardTo,
                ForwardDeadLetteredMessagesTo = sub.Data.ForwardDeadLetteredMessagesTo
            };
        }
    }

    private async Task<ServiceBusNamespaceResource> GetServiceBusNamespaceResourceAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo)
    {
        var armClient = new ArmClient(credential);
        var resourceId = new Azure.Core.ResourceIdentifier(
            $"/subscriptions/{namespaceInfo.SubscriptionId}/resourceGroups/{namespaceInfo.ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{namespaceInfo.Name}");
        return await armClient.GetServiceBusNamespaceResource(resourceId).GetAsync();
    }
}
