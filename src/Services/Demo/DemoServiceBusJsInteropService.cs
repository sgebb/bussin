using System.Text.Json;
using Bussin.Models;
using Microsoft.JSInterop;

namespace Bussin.Services.Demo;

public class DemoServiceBusJsInteropService : IServiceBusJsInteropService
{
    private readonly Dictionary<string, List<ServiceBusMessage>> _store = new();
    private readonly Random _random = new();

    public IJSRuntime JSRuntime => null!; // Not used in demo mode

    public DemoServiceBusJsInteropService()
    {
        SeedData();
    }

    private void SeedData()
    {
        // specific seed for bussin-demo-prod
        // Queues
        AddMessages("bussin-demo-prod", "orders", 5);
        AddMessages("bussin-demo-prod", "notifications", 120);
        
        // Topics/Subscriptions
        AddMessages("bussin-demo-prod", "order-events", "inventory-processor", 2);
        AddMessages("bussin-demo-prod", "order-events", "email-sender", 45);
        
        // bussin-demo-dev
        AddMessages("bussin-demo-dev", "test-queue-1", 10);
        AddMessages("bussin-demo-dev", "dev-events", "sub-1", 5);
    }

    private void AddMessages(string ns, string queue, int count)
    {
        var key = GetKey(ns, queue);
        if (!_store.ContainsKey(key)) _store[key] = new List<ServiceBusMessage>();
        
        for (int i = 0; i < count; i++)
        {
            _store[key].Add(CreateMessage(i));
        }
    }

    private void AddMessages(string ns, string topic, string sub, int count)
    {
        var key = GetKey(ns, topic, sub);
        if (!_store.ContainsKey(key)) _store[key] = new List<ServiceBusMessage>();
        
        for (int i = 0; i < count; i++)
        {
            _store[key].Add(CreateMessage(i));
        }
    }

    private ServiceBusMessage CreateMessage(int i)
    {
        return new ServiceBusMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = JsonSerializer.Serialize(new { id = i, name = $"Item {i}", timestamp = DateTime.UtcNow }),
            ContentType = "application/json",
            EnqueuedTime = DateTime.UtcNow.AddMinutes(-_random.Next(1, 100)),
            SequenceNumber = i,
            DeliveryCount = 1
        };
    }

    private string GetKey(string ns, string queue) => $"{ns}/queues/{queue}";
    private string GetKey(string ns, string topic, string sub) => $"{ns}/topics/{topic}/subscriptions/{sub}";

    // Implementation
    
    public Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
    {
        var key = GetKey(namespaceName, queueName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>());
        return Task.FromResult(msgs.Where(m => (m.SequenceNumber ?? 0) >= fromSequence).Take(count).ToList());
    }

    public Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null)
    {
        var key = GetKey(namespaceName, queueName);
        if (!_store.ContainsKey(key)) _store[key] = new List<ServiceBusMessage>();

        var msg = CreateMessageFromPayload(messageBody, properties);
        _store[key].Add(msg);
        return Task.CompletedTask;
    }

    public Task<int> PurgeQueueAsync(string namespaceName, string queueName, string token, bool fromDeadLetter = false)
    {
        var key = GetKey(namespaceName, queueName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        if (!_store.ContainsKey(key)) return Task.FromResult(0);
        
        var count = _store[key].Count;
        _store[key].Clear();
        return Task.FromResult(count);
    }

    public Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
    {
        var key = GetKey(namespaceName, topicName, subscriptionName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>());
        return Task.FromResult(msgs.Where(m => (m.SequenceNumber ?? 0) >= fromSequence).Take(count).ToList());
    }

    // Helper methods for DemoAzureResourceService
    public int GetQueueMessageCount(string ns, string queue)
    {
        var key = GetKey(ns, queue);
        return _store.ContainsKey(key) ? _store[key].Count : 0;
    }
    
    public int GetQueueDeadLetterCount(string ns, string queue)
    {
        var key = GetKey(ns, queue) + "/$DeadLetterQueue";
        return _store.ContainsKey(key) ? _store[key].Count : 0;
    }

    public int GetSubscriptionMessageCount(string ns, string topic, string sub)
    {
        var key = GetKey(ns, topic, sub);
        return _store.ContainsKey(key) ? _store[key].Count : 0;
    }

    public int GetSubscriptionDeadLetterCount(string ns, string topic, string sub)
    {
        var key = GetKey(ns, topic, sub) + "/$DeadLetterQueue";
        return _store.ContainsKey(key) ? _store[key].Count : 0;
    }

    public Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null)
    {
        // In reality, sending to a topic duplicates to subscriptions.
        // For demo, we might need to find all subscriptions for this topic?
        // Or just fail silently? 
        // Better: Find keys in store that match this topic
        
        // This is tricky because we don't have a list of subscriptions easily available unless we hardcode logic or query the resource service.
        // For simplicity, we just won't see the message appear unless we explicitly added some logic to copy it.
        // But the user expects it to "appear to work".
        // I'll try to find matching keys in _store.
        
        var topicPart = $"{namespaceName}/topics/{topicName}/subscriptions/";
        var keys = _store.Keys.Where(k => k.StartsWith(topicPart) && !k.EndsWith("/$DeadLetterQueue")).ToList();
        
        var msg = CreateMessageFromPayload(messageBody, properties);
        
        foreach (var key in keys)
        {
            // Clone message?
             _store[key].Add(msg); // Shared reference is fine for demo
        }
        
        return Task.CompletedTask;
    }

    public Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, bool fromDeadLetter = false)
    {
        var key = GetKey(namespaceName, topicName, subscriptionName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        if (!_store.ContainsKey(key)) return Task.FromResult(0);
        
        var count = _store[key].Count;
        _store[key].Clear();
        return Task.FromResult(count);
    }

    public Task SendQueueMessageBatchAsync(string namespaceName, string queueName, string token, object[] messages)
    {
        var key = GetKey(namespaceName, queueName);
        if (!_store.ContainsKey(key)) _store[key] = new List<ServiceBusMessage>();

        foreach (var m in messages)
        {
             // simplified handling of batch object
             _store[key].Add(CreateMessageFromPayload(m, null));
        }
        return Task.CompletedTask;
    }

    public Task SendTopicMessageBatchAsync(string namespaceName, string topicName, string token, object[] messages)
    {
        var topicPart = $"{namespaceName}/topics/{topicName}/subscriptions/";
        var keys = _store.Keys.Where(k => k.StartsWith(topicPart) && !k.EndsWith("/$DeadLetterQueue")).ToList();
        
        foreach (var m in messages)
        {
            var msg = CreateMessageFromPayload(m, null);
            foreach (var key in keys)
            {
                _store[key].Add(msg);
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
    {
        var key = GetKey(namespaceName, queueName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>()).Take(count).ToList();
        foreach (var m in msgs)
        {
            m.LockToken = Guid.NewGuid().ToString(); // Assign fake lock token
            m.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        }
        return Task.FromResult(msgs);
    }

    public Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
    {
        var key = GetKey(namespaceName, topicName, subscriptionName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>()).Take(count).ToList();
        foreach (var m in msgs)
        {
            m.LockToken = Guid.NewGuid().ToString();
             m.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        }
        return Task.FromResult(msgs);
    }

    public Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens)
    {
        // Find messages with these lock tokens and remove them
        int count = 0;
        foreach (var list in _store.Values)
        {
            count += list.RemoveAll(m => lockTokens.Contains(m.LockToken));
        }
        return Task.FromResult(new BatchOperationResult { SuccessCount = count });
    }

    public Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
    {
        // Just clear the lock token
        foreach (var list in _store.Values)
        {
            foreach (var m in list.Where(m => lockTokens.Contains(m.LockToken)))
            {
                m.LockToken = null;
                m.LockedUntil = null;
            }
        }
        return Task.FromResult(new BatchOperationResult { SuccessCount = lockTokens.Length });
    }

    public Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null)
    {
        var successCount = 0;
        // Move to DLQ
        // We need to know which list they came from to move them to the corresponding DLQ
        
        // Inefficient lookup
        foreach (var key in _store.Keys.ToList())
        {
            if (key.EndsWith("/$DeadLetterQueue")) continue;
            
            var list = _store[key];
            var moving = list.Where(m => lockTokens.Contains(m.LockToken)).ToList();
            
            if (moving.Any())
            {
                var dlqKey = key + "/$DeadLetterQueue";
                if (!_store.ContainsKey(dlqKey)) _store[dlqKey] = new List<ServiceBusMessage>();
                
                foreach (var m in moving)
                {
                    m.LockToken = null;
                    list.Remove(m);
                    _store[dlqKey].Add(m);
                    successCount++;
                }
            }
        }
        return Task.FromResult(new BatchOperationResult { SuccessCount = successCount });
    }

    public Task<IJSObjectReference> StartMonitoringQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task<IJSObjectReference> StartMonitoringSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task StopMonitoringAsync(IJSObjectReference monitorController) => Task.CompletedTask;

    public Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
        return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
         return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task DeleteQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        // TODO: Implement if needed
        return Task.CompletedTask;
    }

    public Task DeleteSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        return Task.CompletedTask;
    }

    public Task DeadLetterQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, string reason = "Manual dead letter", string description = "Moved by user")
    {
        return Task.CompletedTask;
    }

    public Task DeadLetterSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, string reason = "Manual dead letter", string description = "Moved by user")
    {
        return Task.CompletedTask;
    }

    public Task<IJSObjectReference> StartSearchQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
    {
         return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task<IJSObjectReference> StartSearchSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
    {
         return Task.FromResult<IJSObjectReference>(new DemoJSObjectReference());
    }

    public Task<List<ServiceBusMessage>> PeekQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        return Task.FromResult(new List<ServiceBusMessage>());
    }

    public Task<List<ServiceBusMessage>> PeekSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        return Task.FromResult(new List<ServiceBusMessage>());
    }

    private ServiceBusMessage CreateMessageFromPayload(object payload, MessageProperties? props)
    {
        return new ServiceBusMessage
        {
            MessageId = props?.MessageId ?? Guid.NewGuid().ToString(),
            Body = payload is string s ? s : JsonSerializer.Serialize(payload),
            ContentType = props?.ContentType ?? "application/json",
            ApplicationProperties = props?.ApplicationProperties ?? new Dictionary<string,object>(),
            EnqueuedTime = DateTime.UtcNow,
            SequenceNumber = DateTime.UtcNow.Ticks, // approximate unique
            DeliveryCount = 0
        };
    }
}

public class DemoJSObjectReference : IJSObjectReference
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    public ValueTask InvokeVoidAsync(string identifier, object?[]? args) => ValueTask.CompletedTask;
    public ValueTask InvokeVoidAsync(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
