using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ServiceBus;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class AzureResourceService : IAzureResourceService
{
    public async Task<List<ServiceBusNamespaceInfo>> ListServiceBusNamespacesAsync(TokenCredential credential)
    {
        var armClient = new ArmClient(credential);
        var namespaces = new List<ServiceBusNamespaceInfo>();

        try
        {
            Console.WriteLine("Starting to list subscriptions...");
            var subscriptionCount = 0;
            
            await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync())
            {
                subscriptionCount++;
                Console.WriteLine($"Found subscription: {subscription.Data.DisplayName} ({subscription.Data.SubscriptionId})");
                
                var nsCount = 0;
                await foreach (var ns in subscription.GetServiceBusNamespacesAsync())
                {
                    nsCount++;
                    var data = ns.Data;
                    Console.WriteLine($"  Found namespace: {data.Name} in {ns.Id.ResourceGroupName}");
                    
                    namespaces.Add(new ServiceBusNamespaceInfo
                    {
                        Name = data.Name,
                        FullyQualifiedNamespace = data.ServiceBusEndpoint?.Replace("https://", "").Replace(":443/", "") ?? $"{data.Name}.servicebus.windows.net",
                        ResourceGroup = ns.Id.ResourceGroupName ?? "Unknown",
                        SubscriptionId = subscription.Data.SubscriptionId ?? "Unknown",
                        Location = data.Location.Name
                    });
                }
                Console.WriteLine($"  Total namespaces in this subscription: {nsCount}");
            }
            
            Console.WriteLine($"Total subscriptions checked: {subscriptionCount}");
            Console.WriteLine($"Total namespaces found: {namespaces.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing namespaces: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        return namespaces;
    }

    public async Task<List<string>> ListQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo)
    {
        var armClient = new ArmClient(credential);
        var queues = new List<string>();

        try
        {
            var subscriptionId = namespaceInfo.SubscriptionId;
            var resourceGroup = namespaceInfo.ResourceGroup;
            var namespaceName = namespaceInfo.Name;

            var subscription = await armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();
            var resourceGroupResource = await subscription.Value.GetResourceGroupAsync(resourceGroup);
            var serviceBusNamespace = await resourceGroupResource.Value.GetServiceBusNamespaceAsync(namespaceName);

            await foreach (var queue in serviceBusNamespace.Value.GetServiceBusQueues().GetAllAsync())
            {
                queues.Add(queue.Data.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing queues: {ex.Message}");
        }

        return queues;
    }

    public async Task<List<string>> ListTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo)
    {
        var armClient = new ArmClient(credential);
        var topics = new List<string>();

        try
        {
            var subscriptionId = namespaceInfo.SubscriptionId;
            var resourceGroup = namespaceInfo.ResourceGroup;
            var namespaceName = namespaceInfo.Name;

            var subscription = await armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();
            var resourceGroupResource = await subscription.Value.GetResourceGroupAsync(resourceGroup);
            var serviceBusNamespace = await resourceGroupResource.Value.GetServiceBusNamespaceAsync(namespaceName);

            await foreach (var topic in serviceBusNamespace.Value.GetServiceBusTopics().GetAllAsync())
            {
                topics.Add(topic.Data.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing topics: {ex.Message}");
        }

        return topics;
    }

    public async Task<List<string>> ListSubscriptionsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName)
    {
        var armClient = new ArmClient(credential);
        var subscriptions = new List<string>();

        try
        {
            var subscriptionId = namespaceInfo.SubscriptionId;
            var resourceGroup = namespaceInfo.ResourceGroup;
            var namespaceName = namespaceInfo.Name;

            var subscription = await armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}")).GetAsync();
            var resourceGroupResource = await subscription.Value.GetResourceGroupAsync(resourceGroup);
            var serviceBusNamespace = await resourceGroupResource.Value.GetServiceBusNamespaceAsync(namespaceName);
            var topic = await serviceBusNamespace.Value.GetServiceBusTopicAsync(topicName);

            await foreach (var sub in topic.Value.GetServiceBusSubscriptions().GetAllAsync())
            {
                subscriptions.Add(sub.Data.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing subscriptions: {ex.Message}");
        }

        return subscriptions;
    }
}
