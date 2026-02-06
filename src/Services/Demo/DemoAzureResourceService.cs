using Azure.Core;
using Bussin.Models;
using Bussin.Services;

namespace Bussin.Services.Demo;

public class DemoAzureResourceService : IAzureResourceService
{
    private readonly List<ServiceBusNamespaceInfo> _namespaces;
    private readonly DemoServiceBusJsInteropService _demoJsService;

    public DemoAzureResourceService(DemoServiceBusJsInteropService demoJsService)
    {
        _demoJsService = demoJsService;
        _namespaces = new List<ServiceBusNamespaceInfo>
        {
            new ServiceBusNamespaceInfo
            {
                Name = "bussin-demo-prod",
                FullyQualifiedNamespace = "bussin-demo-prod.servicebus.windows.net",
                ResourceGroup = "bussin-demo-rg",
                SubscriptionId = "00000000-0000-0000-0000-000000000000",
                SubscriptionName = "Demo Subscription",
                Location = "West Europe"
            },
            new ServiceBusNamespaceInfo
            {
                Name = "bussin-demo-dev",
                FullyQualifiedNamespace = "bussin-demo-dev.servicebus.windows.net",
                ResourceGroup = "bussin-demo-rg",
                SubscriptionId = "00000000-0000-0000-0000-000000000000",
                SubscriptionName = "Demo Subscription",
                Location = "West Europe"
            }
        };
    }

    public async IAsyncEnumerable<ServiceBusNamespaceInfo> ListServiceBusNamespacesAsync(TokenCredential credential, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var ns in _namespaces)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return ns;
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceBusQueueInfo> ListQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        if (namespaceInfo.Name == "bussin-demo-prod")
        {
            yield return CreateQueue(namespaceInfo.Name, "orders");
            yield return CreateQueue(namespaceInfo.Name, "notifications");
            yield return CreateQueue(namespaceInfo.Name, "audit-log");
        }
        else
        {
            yield return CreateQueue(namespaceInfo.Name, "test-queue-1");
            yield return CreateQueue(namespaceInfo.Name, "test-queue-2");
        }
        await Task.CompletedTask;
    }

    private ServiceBusQueueInfo CreateQueue(string ns, string name)
    {
        return new ServiceBusQueueInfo 
        { 
            Name = name, 
            Status = "Active", 
            ActiveMessageCount = _demoJsService.GetQueueMessageCount(ns, name), 
            DeadLetterMessageCount = _demoJsService.GetQueueDeadLetterCount(ns, name) 
        };
    }

    public async IAsyncEnumerable<ServiceBusTopicInfo> ListTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        if (namespaceInfo.Name == "bussin-demo-prod")
        {
            yield return new ServiceBusTopicInfo { Name = "order-events", Status = "Active", SubscriptionCount = 2 };
            yield return new ServiceBusTopicInfo { Name = "user-updates", Status = "Active", SubscriptionCount = 1 };
        }
        else
        {
            yield return new ServiceBusTopicInfo { Name = "dev-events", Status = "Active", SubscriptionCount = 1 };
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceBusSubscriptionInfo> ListSubscriptionsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        if (namespaceInfo.Name == "bussin-demo-prod" && topicName == "order-events")
        {
            yield return CreateSubscription(namespaceInfo.Name, topicName, "inventory-processor");
            yield return CreateSubscription(namespaceInfo.Name, topicName, "email-sender");
        }
        else if (namespaceInfo.Name == "bussin-demo-prod" && topicName == "user-updates")
        {
            yield return CreateSubscription(namespaceInfo.Name, topicName, "cache-invalidator");
        }
        else if (namespaceInfo.Name == "bussin-demo-dev" && topicName == "dev-events")
        {
            yield return CreateSubscription(namespaceInfo.Name, topicName, "sub-1");
        }
        await Task.CompletedTask;
    }

    private ServiceBusSubscriptionInfo CreateSubscription(string ns, string topic, string subName)
    {
        return new ServiceBusSubscriptionInfo 
        { 
            Name = subName, 
            Status = "Active", 
            ActiveMessageCount = _demoJsService.GetSubscriptionMessageCount(ns, topic, subName), 
            DeadLetterMessageCount = _demoJsService.GetSubscriptionDeadLetterCount(ns, topic, subName) 
        };
    }
}
