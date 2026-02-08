using System.Text.Json;
using Bussin.Models;
using Microsoft.JSInterop;

namespace Bussin.Services.Demo;

public class DemoServiceBusJsInteropService : IServiceBusJsInteropService
{
    private readonly Dictionary<string, List<ServiceBusMessage>> _store = new();
    private readonly Random _random = new();

    public IJSRuntime JSRuntime { get; }

    public DemoServiceBusJsInteropService(IJSRuntime jsRuntime)
    {
        JSRuntime = jsRuntime;
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
        var topicPart = $"{namespaceName}/topics/{topicName}/subscriptions/";
        var keys = _store.Keys.Where(k => k.StartsWith(topicPart) && !k.EndsWith("/$DeadLetterQueue")).ToList();
        
        var msg = CreateMessageFromPayload(messageBody, properties);
        
        foreach (var key in keys)
        {
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
        int count = 0;
        foreach (var list in _store.Values)
        {
            count += list.RemoveAll(m => lockTokens.Contains(m.LockToken));
        }
        return Task.FromResult(new BatchOperationResult { SuccessCount = count });
    }

    public Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
    {
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

    public async Task<IJSObjectReference> StartMonitoringQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", (object?)null);
    }

    public async Task<IJSObjectReference> StartMonitoringSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", (object?)null);
    }

    public Task StopMonitoringAsync(IJSObjectReference monitorController) => Task.CompletedTask;

    public async Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
        var count = await PurgeQueueAsync(namespaceName, queueName, token, fromDeadLetter);
        callbackRef.Value.OnProgress(count);
        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", count);
    }

    public async Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
        var count = await PurgeSubscriptionAsync(namespaceName, topicName, subscriptionName, token, fromDeadLetter);
        callbackRef.Value.OnProgress(count);
        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", count);
    }

    public Task DeleteQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
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

    public async Task<IJSObjectReference> StartSearchQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
    {
        var key = GetKey(namespaceName, queueName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>());
        var matches = msgs.Take(maxMatches).Select(m => m.SequenceNumber ?? 0L).ToArray();
        
        callbackRef.Value.OnProgress(msgs.Count, matches.Length, matches.Take(10).ToArray());

        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", new SearchResult 
        { 
            ScannedCount = msgs.Count, 
            MatchCount = matches.Length, 
            MatchingSequenceNumbers = matches 
        });
    }

    public async Task<IJSObjectReference> StartSearchSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
    {
        var key = GetKey(namespaceName, topicName, subscriptionName) + (fromDeadLetter ? "/$DeadLetterQueue" : "");
        var msgs = _store.GetValueOrDefault(key, new List<ServiceBusMessage>());
        var matches = msgs.Take(maxMatches).Select(m => m.SequenceNumber ?? 0L).ToArray();
        
        callbackRef.Value.OnProgress(msgs.Count, matches.Length, matches.Take(10).ToArray());

        return await JSRuntime.InvokeAsync<IJSObjectReference>("createMockController", new SearchResult 
        { 
            ScannedCount = msgs.Count, 
            MatchCount = matches.Length, 
            MatchingSequenceNumbers = matches 
        });
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
