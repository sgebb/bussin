using Bussin.Models;

namespace Bussin.Services;

public interface IAzureResourceService
{
    IAsyncEnumerable<ServiceBusNamespaceInfo> ListServiceBusNamespacesAsync(ArmToken credential, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusQueueInfo> ListQueuesAsync(ArmToken credential, ServiceBusNamespaceInfo namespaceInfo, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusTopicInfo> ListTopicsAsync(ArmToken credential, ServiceBusNamespaceInfo namespaceInfo, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ServiceBusSubscriptionInfo> ListSubscriptionsAsync(ArmToken credential, ServiceBusNamespaceInfo namespaceInfo, string topicName, CancellationToken cancellationToken = default);
    
    IAsyncEnumerable<TenantInfo> ListTenantsAsync(ArmToken credential, CancellationToken cancellationToken = default);
}
