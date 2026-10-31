using Bussin.Models;
using Microsoft.JSInterop;

namespace Bussin.Services.Demo;

/// <summary>
/// The real interop service with the client-js simulator (an in-browser mock broker) switched on and seeded.
/// </summary>
public class DemoServiceBusJsInteropService(IJSRuntime jsRuntime) : ServiceBusJsInteropService(jsRuntime)
{
    private IJSInProcessRuntime? InProcessRuntime => JSRuntime as IJSInProcessRuntime;
    private bool _isInitialized = false;

    public long GetQueueMessageCount(string ns, string name) => InProcessRuntime?.Invoke<long>("ServiceBusAPI.getQueueMessageCount", ns, name, false) ?? 0;
    public long GetQueueDeadLetterCount(string ns, string name) => InProcessRuntime?.Invoke<long>("ServiceBusAPI.getQueueMessageCount", ns, name, true) ?? 0;
    public long GetSubscriptionMessageCount(string ns, string topic, string sub) => InProcessRuntime?.Invoke<long>("ServiceBusAPI.getSubscriptionMessageCount", ns, topic, sub, false) ?? 0;
    public long GetSubscriptionDeadLetterCount(string ns, string topic, string sub) => InProcessRuntime?.Invoke<long>("ServiceBusAPI.getSubscriptionMessageCount", ns, topic, sub, true) ?? 0;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.enableSimulator", true);
        await SeedInitialDataAsync();

        _isInitialized = true;
    }

    protected override Task EnsureReadyAsync() => InitializeAsync();

    private async Task SeedInitialDataAsync()
    {
        // 1. Seed Production Namespace (bussin-demo-prod)
        var prodNs = "bussin-demo-prod";
        
        // Orders Queue
        var orderMessages = new[] {
            new { body = new { orderId = "ORD-101", customer = "Alice", total = 42.50 }, messageId = "msg-ord-1", properties = new { subject = "New Order", content_type = (string?)"application/json" } },
            new { body = new { orderId = "ORD-102", customer = "Bob", total = 12.99 }, messageId = "msg-ord-2", properties = new { subject = "New Order", content_type = (string?)null } }
        };
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedMockData", prodNs, "orders", orderMessages);
        
        // Notifications
        var notificationMessages = new[] {
            new { body = "Welcome to Bussin!", messageId = "msg-notif-1" },
            new { body = "Your order was shipped", messageId = "msg-notif-2" }
        };
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedMockData", prodNs, "notifications", notificationMessages);

        // Order Events (Topic)
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedTopic", prodNs, "order-events");
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedSubscription", prodNs, "order-events", "inventory-processor");
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedSubscription", prodNs, "order-events", "email-sender");
        
        var topicMessages = new[] {
            new { body = new { @event = "OrderPlaced", id = "ORD-101" }, messageId = "evt-1" },
            new { body = new { @event = "PaymentSuccessful", id = "ORD-101" }, messageId = "evt-2" }
        };
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedTopicData", prodNs, "order-events", topicMessages);

        // 2. Seed Development Namespace (bussin-demo-dev)
        var devNs = "bussin-demo-dev";
        
        var devMessages = new[] {
            new { body = "Test message 1", messageId = "test-1" },
            new { body = "Test message 2", messageId = "test-2" },
            new { body = "Test message 3", messageId = "test-3" }
        };
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedMockData", devNs, "test-queue-1", devMessages);

        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedTopic", devNs, "dev-events");
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedSubscription", devNs, "dev-events", "sub-1");
        
        var subMessages = new[] {
            new { body = "Secret event for sub-1", messageId = "sub-evt-1" }
        };
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedSubscriptionData", devNs, "dev-events", "sub-1", subMessages);
    }
}
