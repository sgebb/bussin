using System.Net;
using System.Text;
using Bussin.Models;
using Bussin.Services;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class AzureResourceServiceTests
{
    private static readonly ArmToken Token = new("token", DateTimeOffset.UtcNow.AddHours(1));

    private static readonly ServiceBusNamespaceInfo Namespace = new()
    {
        Name = "contoso",
        FullyQualifiedNamespace = "contoso.servicebus.windows.net",
        ResourceGroup = "rg",
        SubscriptionId = "sub-1"
    };

    /// <summary>Answers each request by path (without the query string); unknown paths fail.</summary>
    private sealed class FakeArm(Dictionary<string, Func<HttpResponseMessage>> routes) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requested.Add(request.RequestUri.PathAndQuery);
            Assert.Equal("Bearer token", request.Headers.Authorization?.ToString());
            return Task.FromResult(routes.TryGetValue(path, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"no route for {path}") });
        }
    }

    private static Func<HttpResponseMessage> Json(string json) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Queues(params string[] names) =>
        "{\"value\":[" + string.Join(",", names.Select(n => $"{{\"name\":\"{n}\",\"properties\":{{\"status\":\"Active\"}}}}")) + "]}";

    private const string QueuesPath = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/contoso/queues";

    private static AzureResourceService Create(ServiceBusEntityCache cache, FakeArm arm) => new(cache, new HttpClient(arm));

    [Fact]
    public async Task MapsQueuePropertiesAndFollowsNextLink()
    {
        var arm = new FakeArm(new()
        {
            [QueuesPath] = Json("""
                {"value":[{"name":"orders","properties":{"status":"Active","requiresSession":true,"enablePartitioning":true,
                  "maxSizeInMegabytes":1024,"countDetails":{"activeMessageCount":5,"deadLetterMessageCount":2}}}],
                 "nextLink":"https://management.azure.com/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/contoso/queues/page2?api-version=2021-11-01"}
                """),
            [QueuesPath + "/page2"] = Json(Queues("invoices"))
        });

        var queues = await Create(new ServiceBusEntityCache(), arm).ListQueuesAsync(Token, Namespace).ToListAsync();

        Assert.Equal(["orders", "invoices"], queues.Select(q => q.Name));
        var orders = queues[0];
        Assert.Equal("Active", orders.Status);
        Assert.Equal(5, orders.ActiveMessageCount);
        Assert.Equal(2, orders.DeadLetterMessageCount);
        Assert.Equal(1024, orders.MaxSizeInMegabytes);
        Assert.True(orders.RequiresSession);
        Assert.True(orders.EnablePartitioning);
    }

    [Fact]
    public async Task AnAbandonedRefreshKeepsTheCachedEntitiesItHasNotReachedYet()
    {
        var cache = new ServiceBusEntityCache();
        await Create(cache, new FakeArm(new() { [QueuesPath] = Json(Queues("a", "b", "c")) }))
            .ListQueuesAsync(Token, Namespace).ToListAsync();

        // The cached snapshot comes first, then the fresh data; stop right after the first fresh item
        var service = Create(cache, new FakeArm(new() { [QueuesPath] = Json(Queues("a", "b", "c")) }));
        var seen = 0;
        await foreach (var _ in service.ListQueuesAsync(Token, Namespace))
        {
            if (++seen == 4) break;
        }

        Assert.Equal(["a", "b", "c"], cache.GetQueues("sub-1/rg/contoso")!.Select(q => q.Name).Order());
    }

    [Fact]
    public async Task ACompletedRefreshDropsEntitiesThatNoLongerExist()
    {
        var cache = new ServiceBusEntityCache();
        await Create(cache, new FakeArm(new() { [QueuesPath] = Json(Queues("a", "b")) }))
            .ListQueuesAsync(Token, Namespace).ToListAsync();

        await Create(cache, new FakeArm(new() { [QueuesPath] = Json(Queues("a")) }))
            .ListQueuesAsync(Token, Namespace).ToListAsync();

        Assert.Equal(["a"], cache.GetQueues("sub-1/rg/contoso")!.Select(q => q.Name));
    }

    [Fact]
    public async Task ListsNamespacesFromEveryReadableSubscription()
    {
        var arm = new FakeArm(new()
        {
            ["/subscriptions"] = Json("""
                {"value":[
                  {"subscriptionId":"sub-1","displayName":"One","tenantId":"t","state":"Enabled"},
                  {"subscriptionId":"sub-2","displayName":"Two","tenantId":"t","state":"Enabled"},
                  {"subscriptionId":"sub-3","displayName":"Off","tenantId":"t","state":"Disabled"}]}
                """),
            ["/subscriptions/sub-1/providers/Microsoft.ServiceBus/namespaces"] = Json("""
                {"value":[{"name":"contoso","location":"West Europe",
                  "id":"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/contoso"}]}
                """),
            ["/subscriptions/sub-2/providers/Microsoft.ServiceBus/namespaces"] =
                () => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("AuthorizationFailed") }
        });

        var namespaces = await Create(new ServiceBusEntityCache(), arm).ListServiceBusNamespacesAsync(Token).ToListAsync();

        var ns = Assert.Single(namespaces);
        Assert.Equal("contoso.servicebus.windows.net", ns.FullyQualifiedNamespace);
        Assert.Equal("rg-1", ns.ResourceGroup);
        Assert.Equal("One", ns.SubscriptionName);
        Assert.Equal("West Europe", ns.Location);
        Assert.DoesNotContain(arm.Requested, r => r.Contains("sub-3"));
    }

    [Fact]
    public async Task FailsWithTheCauseWhenNoSubscriptionCanBeRead()
    {
        var arm = new FakeArm(new()
        {
            ["/subscriptions"] = Json("""{"value":[{"subscriptionId":"sub-1","state":"Enabled"}]}"""),
            ["/subscriptions/sub-1/providers/Microsoft.ServiceBus/namespaces"] =
                () => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("AuthorizationFailed") }
        });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(new ServiceBusEntityCache(), arm).ListServiceBusNamespacesAsync(Token).ToListAsync().AsTask());

        Assert.Contains("403", error.Message);
        Assert.Contains("AuthorizationFailed", error.Message);
    }
}
