using Azure.Core;
using Bussin.Models;

namespace Bussin.Services;

public interface IAzureResourceService
{
    IAsyncEnumerable<ServiceBusNamespaceInfo> ListServiceBusNamespacesAsync(TokenCredential credential, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusQueueInfo> ListQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusTopicInfo> ListTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusSubscriptionInfo> ListSubscriptionsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName, CancellationToken cancellationToken = default);
    
    IAsyncEnumerable<TenantInfo> ListTenantsAsync(TokenCredential credential, CancellationToken cancellationToken = default);
}
