using Bussin.Models;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Bussin.Services;

/// <summary>
/// Lists tenants, namespaces and entities through the Azure Resource Manager REST API.
/// Plain HTTP rather than the Azure.ResourceManager SDKs, which would add over half a megabyte
/// to the WebAssembly download for a handful of read-only GETs.
/// </summary>
public sealed class AzureResourceService : IAzureResourceService
{
    private const string ArmEndpoint = "https://management.azure.com";
    private const string SubscriptionsApiVersion = "2022-12-01";
    private const string ServiceBusApiVersion = "2021-11-01";

    private readonly HttpClient _http;
    private readonly ServiceBusEntityCache _cache;

    public AzureResourceService(ServiceBusEntityCache cache) : this(cache, new HttpClient())
    {
    }

    internal AzureResourceService(ServiceBusEntityCache cache, HttpClient http)
    {
        _cache = cache;
        _http = http;
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
        Func<T, string> key,
        Func<CancellationToken, IAsyncEnumerable<T>> fetchFresh,
        Action<List<T>> updateCache,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        // Yield the cached snapshot for an immediate UI, then replace it with fresh
        // resource data. Callers key these items by name, so the fresh values update
        // rather than duplicate the visible entities.
        if (cached != null && cached.Count > 0)
        {
            foreach (var item in cached)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return item;
            }
        }

        // Fetch a new snapshot even after a cache hit so the in-app refresh and
        // namespace switching paths observe broker changes immediately.
        // While the fetch runs, the cache holds the fresh items seen so far plus the
        // cached ones not seen yet, so a load abandoned midway (navigation, cancellation,
        // error) refreshes what it reached without dropping the rest.
        var freshItems = new List<T>();
        var notSeenYet = new Dictionary<string, T>();
        foreach (var item in cached ?? []) notSeenYet[key(item)] = item;

        await foreach (var item in fetchFresh(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            freshItems.Add(item);
            notSeenYet.Remove(key(item));
            updateCache([.. freshItems, .. notSeenYet.Values]);
            yield return item;
        }

        // Only a completed fetch may drop entities that no longer exist.
        updateCache(freshItems);
    }

    public async IAsyncEnumerable<ServiceBusNamespaceInfo> ListServiceBusNamespacesAsync(
        ArmToken credential,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var ns in CachedStreamAsync(
            _cache.GetNamespaces(),
            ns => ns.FullyQualifiedNamespace,
            ct => FetchNamespacesAsync(credential, ct),
            fresh => _cache.SetNamespaces(fresh),
            cancellationToken))
        {
            yield return ns;
        }
    }

    private async IAsyncEnumerable<ServiceBusNamespaceInfo> FetchNamespacesAsync(
        ArmToken credential,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<JsonElement>();
        await foreach (var sub in ArmListAsync(credential, $"/subscriptions?api-version={SubscriptionsApiVersion}", cancellationToken))
        {
            if (GetString(sub, "state") == "Enabled") subscriptions.Add(sub);
        }

        // Query every subscription at once and show each one's namespaces as soon as it answers.
        var lookups = subscriptions.Select(sub => ListNamespacesInSubscriptionAsync(credential, sub, cancellationToken)).ToList();
        var failures = new List<Exception>();

        await foreach (var lookup in Task.WhenEach(lookups).WithCancellation(cancellationToken))
        {
            if (lookup.IsCanceled) yield break;
            if (lookup.IsFaulted)
            {
                // A subscription the user cannot read Service Bus in contributes nothing to the list.
                var error = lookup.Exception!.InnerException!;
                Console.WriteLine($"Skipping subscription while listing namespaces: {error.Message}");
                failures.Add(error);
                continue;
            }

            foreach (var ns in lookup.Result)
            {
                yield return ns;
            }
        }

        if (failures.Count > 0 && failures.Count == lookups.Count)
        {
            throw failures[0];
        }
    }

    private async Task<List<ServiceBusNamespaceInfo>> ListNamespacesInSubscriptionAsync(
        ArmToken credential,
        JsonElement subscription,
        CancellationToken cancellationToken)
    {
        var subscriptionId = GetString(subscription, "subscriptionId");
        var namespaces = new List<ServiceBusNamespaceInfo>();

        await foreach (var ns in ArmListAsync(credential,
            $"/subscriptions/{subscriptionId}/providers/Microsoft.ServiceBus/namespaces?api-version={ServiceBusApiVersion}",
            cancellationToken))
        {
            var name = GetString(ns, "name");
            namespaces.Add(new ServiceBusNamespaceInfo
            {
                Name = name,
                FullyQualifiedNamespace = $"{name}.servicebus.windows.net",
                // /subscriptions/<id>/resourceGroups/<rg>/providers/...
                ResourceGroup = GetString(ns, "id").Split("/resourceGroups/")[1].Split('/')[0],
                SubscriptionId = subscriptionId,
                SubscriptionName = GetString(subscription, "displayName"),
                Location = GetString(ns, "location"),
                TenantId = GetString(subscription, "tenantId")
            });
        }

        return namespaces;
    }

    public async IAsyncEnumerable<ServiceBusQueueInfo> ListQueuesAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(namespaceInfo);

        await foreach (var queue in CachedStreamAsync(
            _cache.GetQueues(cacheKey),
            q => q.Name,
            ct => FetchQueuesAsync(credential, namespaceInfo, ct),
            fresh => _cache.SetQueues(cacheKey, fresh),
            cancellationToken))
        {
            yield return queue;
        }
    }

    private async IAsyncEnumerable<ServiceBusQueueInfo> FetchQueuesAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var namespacePath = GetNamespacePath(namespaceInfo);
        if (namespacePath == null) yield break;

        await foreach (var queue in ArmListAsync(credential, $"{namespacePath}/queues?api-version={ServiceBusApiVersion}", cancellationToken))
        {
            var p = queue.GetProperty("properties");
            var counts = GetObject(p, "countDetails");
            yield return new ServiceBusQueueInfo
            {
                Name = GetString(queue, "name"),
                Status = GetString(p, "status", "Unknown"),
                ActiveMessageCount = GetLong(counts, "activeMessageCount"),
                DeadLetterMessageCount = GetLong(counts, "deadLetterMessageCount"),
                ScheduledMessageCount = GetLong(counts, "scheduledMessageCount"),
                TransferMessageCount = GetLong(counts, "transferMessageCount"),
                TransferDeadLetterMessageCount = GetLong(counts, "transferDeadLetterMessageCount"),
                MaxSizeInMegabytes = GetLong(p, "maxSizeInMegabytes"),
                SizeInBytes = GetLong(p, "sizeInBytes"),
                RequiresSession = GetBool(p, "requiresSession"),
                EnablePartitioning = GetBool(p, "enablePartitioning"),
                RequiresDuplicateDetection = GetBool(p, "requiresDuplicateDetection"),
                DeadLetteringOnMessageExpiration = GetBool(p, "deadLetteringOnMessageExpiration"),
                ForwardTo = GetOptionalString(p, "forwardTo"),
                ForwardDeadLetteredMessagesTo = GetOptionalString(p, "forwardDeadLetteredMessagesTo")
            };
        }
    }

    public async IAsyncEnumerable<ServiceBusTopicInfo> ListTopicsAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(namespaceInfo);

        await foreach (var topic in CachedStreamAsync(
            _cache.GetTopics(cacheKey),
            t => t.Name,
            ct => FetchTopicsAsync(credential, namespaceInfo, ct),
            fresh => _cache.SetTopics(cacheKey, fresh),
            cancellationToken))
        {
            yield return topic;
        }
    }

    private async IAsyncEnumerable<ServiceBusTopicInfo> FetchTopicsAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var namespacePath = GetNamespacePath(namespaceInfo);
        if (namespacePath == null) yield break;

        await foreach (var topic in ArmListAsync(credential, $"{namespacePath}/topics?api-version={ServiceBusApiVersion}", cancellationToken))
        {
            var p = topic.GetProperty("properties");
            yield return new ServiceBusTopicInfo
            {
                Name = GetString(topic, "name"),
                Status = GetString(p, "status", "Unknown"),
                ScheduledMessageCount = GetLong(GetObject(p, "countDetails"), "scheduledMessageCount"),
                MaxSizeInMegabytes = GetLong(p, "maxSizeInMegabytes"),
                SizeInBytes = GetLong(p, "sizeInBytes"),
                SubscriptionCount = (int)GetLong(p, "subscriptionCount")
            };
        }
    }

    public async IAsyncEnumerable<ServiceBusSubscriptionInfo> ListSubscriptionsAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string topicName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var cacheKey = GetSubscriptionCacheKey(namespaceInfo, topicName);

        await foreach (var sub in CachedStreamAsync(
            _cache.GetSubscriptions(cacheKey),
            s => s.Name,
            ct => FetchSubscriptionsAsync(credential, namespaceInfo, topicName, ct),
            fresh => _cache.SetSubscriptions(cacheKey, fresh),
            cancellationToken))
        {
            yield return sub;
        }
    }

    private async IAsyncEnumerable<ServiceBusSubscriptionInfo> FetchSubscriptionsAsync(
        ArmToken credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string topicName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var namespacePath = GetNamespacePath(namespaceInfo);
        if (namespacePath == null) yield break;

        var path = $"{namespacePath}/topics/{Uri.EscapeDataString(topicName)}/subscriptions?api-version={ServiceBusApiVersion}";
        await foreach (var sub in ArmListAsync(credential, path, cancellationToken))
        {
            var p = sub.GetProperty("properties");
            var counts = GetObject(p, "countDetails");
            yield return new ServiceBusSubscriptionInfo
            {
                Name = GetString(sub, "name"),
                Status = GetString(p, "status", "Unknown"),
                ActiveMessageCount = GetLong(counts, "activeMessageCount"),
                DeadLetterMessageCount = GetLong(counts, "deadLetterMessageCount"),
                TransferMessageCount = GetLong(counts, "transferMessageCount"),
                TransferDeadLetterMessageCount = GetLong(counts, "transferDeadLetterMessageCount"),
                RequiresSession = GetBool(p, "requiresSession"),
                DeadLetteringOnMessageExpiration = GetBool(p, "deadLetteringOnMessageExpiration"),
                ForwardTo = GetOptionalString(p, "forwardTo"),
                ForwardDeadLetteredMessagesTo = GetOptionalString(p, "forwardDeadLetteredMessagesTo")
            };
        }
    }

    public async IAsyncEnumerable<TenantInfo> ListTenantsAsync(
        ArmToken credential,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // We don't cache tenants as this is a rare operation and the list is small
        await foreach (var tenant in ArmListAsync(credential, $"/tenants?api-version={SubscriptionsApiVersion}", cancellationToken))
        {
            yield return new TenantInfo
            {
                TenantId = GetString(tenant, "tenantId"),
                DisplayName = GetOptionalString(tenant, "displayName"),
                DefaultDomain = GetOptionalString(tenant, "defaultDomain"),
                TenantType = GetOptionalString(tenant, "tenantType")
            };
        }
    }

    private static string? GetNamespacePath(ServiceBusNamespaceInfo namespaceInfo)
    {
        if (string.IsNullOrEmpty(namespaceInfo.SubscriptionId) ||
            string.IsNullOrEmpty(namespaceInfo.ResourceGroup) ||
            string.IsNullOrEmpty(namespaceInfo.Name))
        {
            // Incomplete info shows up during rapid navigation; there is nothing to list yet.
            return null;
        }

        return $"/subscriptions/{namespaceInfo.SubscriptionId}/resourceGroups/{namespaceInfo.ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{namespaceInfo.Name}";
    }

    /// <summary>
    /// GETs an ARM list endpoint and yields every item of every page, following nextLink.
    /// </summary>
    private async IAsyncEnumerable<JsonElement> ArmListAsync(
        ArmToken credential,
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (string? url = ArmEndpoint + path; url != null;)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Azure Resource Manager request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {body}",
                    null,
                    response.StatusCode);
            }

            using var page = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            url = GetOptionalString(page.RootElement, "nextLink");

            foreach (var item in page.RootElement.GetProperty("value").EnumerateArray())
            {
                yield return item.Clone();
            }
        }
    }

    private static JsonElement GetObject(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GetString(JsonElement element, string name, string fallback = "") =>
        GetOptionalString(element, name) ?? fallback;

    private static long GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;

    private static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
