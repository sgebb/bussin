using Azure.Core;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public interface IAzureResourceService
{
    Task<List<ServiceBusNamespaceInfo>> ListServiceBusNamespacesAsync(TokenCredential credential);
    Task<List<ServiceBusQueueInfo>> ListQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo);
    Task<List<ServiceBusTopicInfo>> ListTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo);
    Task<List<ServiceBusSubscriptionInfo>> ListSubscriptionsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName);
}
