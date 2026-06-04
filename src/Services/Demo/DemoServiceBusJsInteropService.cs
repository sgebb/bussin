using System.Text.Json;
using Bussin.Models;
using Microsoft.JSInterop;

namespace Bussin.Services.Demo;

/**
 * High-fidelity Demo Service that uses the TypeScript AMQP Simulator
 */
public class DemoServiceBusJsInteropService(IJSRuntime jsRuntime) : IServiceBusJsInteropService
{
    public IJSRuntime JSRuntime { get; } = jsRuntime;
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

    private async Task EnsureSimulatorAsync() => await InitializeAsync();

    private async Task<T> InvokeSimulatorAsync<T>(string method, params object?[] args)
    {
        await EnsureSimulatorAsync();
        return await JSRuntime.InvokeAsync<T>($"ServiceBusAPI.{method}", args);
    }

    private async Task InvokeSimulatorVoidAsync(string method, params object?[] args)
    {
        await EnsureSimulatorAsync();
        await JSRuntime.InvokeAsync<object>($"ServiceBusAPI.{method}", args);
    }

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

    // Proxy everything to the real JS API

    public Task SetupEmulatorAsync(object? options) => Task.CompletedTask;

    public async Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("peekQueueMessages", namespaceName, queueName, token, count, fromSequence, fromDeadLetter);

    public async Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null)
        => await InvokeSimulatorVoidAsync("sendQueueMessage", namespaceName, queueName, token, messageBody, properties);

    public async ValueTask CreateTopic(string @namespace, string topicName)
    {
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedTopic", @namespace, topicName);
    }

    public async ValueTask CreateSubscription(string @namespace, string topicName, string subscriptionName)
    {
        await JSRuntime.InvokeAsync<object>("ServiceBusAPI.seedSubscription", @namespace, topicName, subscriptionName);
    }

    public async Task<int> PurgeQueueAsync(string namespaceName, string queueName, string token, bool fromDeadLetter = false)
    {
        var result = await InvokeSimulatorAsync<JsonElement>("purgeQueueDirect", namespaceName, queueName, token, fromDeadLetter);
        return result.TryGetProperty("deletedCount", out var prop) ? prop.GetInt32() : 0;
    }

    public async Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("peekSubscriptionMessages", namespaceName, topicName, subscriptionName, token, count, fromSequence, fromDeadLetter);

    public async Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null)
        => await InvokeSimulatorVoidAsync("sendTopicMessage", namespaceName, topicName, token, messageBody, properties);

    public async Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, bool fromDeadLetter = false)
    {
        var result = await InvokeSimulatorAsync<JsonElement>("purgeSubscriptionDirect", namespaceName, topicName, subscriptionName, token, fromDeadLetter);
        return result.TryGetProperty("deletedCount", out var prop) ? prop.GetInt32() : 0;
    }

    public async Task SendQueueMessageBatchAsync(string namespaceName, string queueName, string token, object[] messages)
    {
        await InvokeSimulatorVoidAsync("sendQueueMessageBatch", namespaceName, queueName, token, messages);
    }

    public async Task SendTopicMessageBatchAsync(string namespaceName, string topicName, string token, object[] messages)
    {
        await InvokeSimulatorVoidAsync("sendTopicMessageBatch", namespaceName, topicName, token, messages);
    }

    public async Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("receiveAndLockQueueMessage", namespaceName, queueName, token, timeoutSeconds, fromDeadLetter, count);

    public async Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("receiveAndLockSubscriptionMessage", namespaceName, topicName, subscriptionName, token, timeoutSeconds, fromDeadLetter, count);

    public async Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens)
        => await InvokeSimulatorAsync<BatchOperationResult>("complete", (object)lockTokens);

    public async Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
        => await InvokeSimulatorAsync<BatchOperationResult>("abandon", (object)lockTokens);

    public async Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null)
        => await InvokeSimulatorAsync<BatchOperationResult>("deadLetter", (object)lockTokens, options);

    public async Task<IJSObjectReference> StartMonitoringQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
        => await InvokeSimulatorAsync<IJSObjectReference>("monitorQueue", namespaceName, queueName, token, callbackRef);

    public async Task<IJSObjectReference> StartMonitoringSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
        => await InvokeSimulatorAsync<IJSObjectReference>("monitorSubscription", namespaceName, topicName, subscriptionName, token, callbackRef);

    public async Task StopMonitoringAsync(IJSObjectReference monitorController)
    {
        if (monitorController != null) await monitorController.InvokeVoidAsync("stop");
    }

    public async Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<IJSObjectReference>("purgeQueue", namespaceName, queueName, token, callbackRef, fromDeadLetter);

    public async Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<IJSObjectReference>("purgeSubscription", namespaceName, topicName, subscriptionName, token, callbackRef, fromDeadLetter);

    public async Task DeleteQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => await InvokeSimulatorVoidAsync("deleteQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, fromDeadLetter);

    public async Task DeleteSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => await InvokeSimulatorVoidAsync("deleteSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, fromDeadLetter);

    public async Task DeadLetterQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, string reason = "Manual dead letter", string description = "Moved by user")
        => await InvokeSimulatorVoidAsync("deadLetterQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, reason, description);

    public async Task DeadLetterSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, string reason = "Manual dead letter", string description = "Moved by user")
        => await InvokeSimulatorVoidAsync("deadLetterSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, reason, description);

    public async Task<IJSObjectReference> StartSearchQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
        => await InvokeSimulatorAsync<IJSObjectReference>("searchQueueMessages", namespaceName, queueName, token, callbackRef, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches);

    public async Task<IJSObjectReference> StartSearchSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
        => await InvokeSimulatorAsync<IJSObjectReference>("searchSubscriptionMessages", namespaceName, topicName, subscriptionName, token, callbackRef, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches);

    public async Task<List<ServiceBusMessage>> PeekQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("peekQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, fromDeadLetter);

    public async Task<List<ServiceBusMessage>> PeekSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => await InvokeSimulatorAsync<List<ServiceBusMessage>>("peekSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, fromDeadLetter);
}
