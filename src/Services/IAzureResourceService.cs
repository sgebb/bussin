using Azure.Core;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public interface IAzureResourceService
{
    Task<List<ServiceBusNamespaceInfo>> ListServiceBusNamespacesAsync(TokenCredential credential);
    Task<List<string>> ListQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo);
    Task<List<string>> ListTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo);
    Task<List<string>> ListSubscriptionsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName);
}
