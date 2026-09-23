using Bussin.Models;
using Bussin.Services;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class ServiceBusEntityCacheTests
{
    [Fact]
    public void QueueSnapshot_IsNotMutableThroughTheReturnedList()
    {
        var cache = new ServiceBusEntityCache();
        var queue = new ServiceBusQueueInfo { Name = "orders", Status = "Active" };
        cache.SetQueues("namespace", [queue]);

        var firstRead = cache.GetQueues("namespace");
        Assert.NotNull(firstRead);
        firstRead.Clear();

        var secondRead = cache.GetQueues("namespace");
        Assert.NotNull(secondRead);
        Assert.Single(secondRead);
        Assert.Same(queue, secondRead[0]);
    }

    [Fact]
    public void Clear_RemovesAllSnapshots()
    {
        var cache = new ServiceBusEntityCache();
        cache.SetNamespaces([new ServiceBusNamespaceInfo
        {
            Name = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net",
            ResourceGroup = "rg",
            SubscriptionId = "subscription"
        }]);
        cache.SetTopics("namespace", [new ServiceBusTopicInfo { Name = "events", Status = "Active" }]);

        cache.Clear();

        Assert.Null(cache.GetNamespaces());
        Assert.Null(cache.GetTopics("namespace"));
    }
}
