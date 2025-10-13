using Microsoft.JSInterop;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public interface IServiceBusJsInteropService
{
    // Queue Operations
    Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false);
    Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null);
    Task SendScheduledQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, DateTime scheduledEnqueueTime, MessageProperties? properties = null);
    Task<int> PurgeQueueAsync(string namespaceName, string queueName, string token, bool fromDeadLetter = false);
    
    // Topic/Subscription Operations
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false);
    Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null);
    Task SendScheduledTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, DateTime scheduledEnqueueTime, MessageProperties? properties = null);
    Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, bool fromDeadLetter = false);
    
    // Lock-based operations (new API)
    Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1);
    Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1);
    Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null);
    
    // Monitor operations (continuous non-destructive)
    Task<IJSObjectReference> StartMonitoringQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef);
    Task<IJSObjectReference> StartMonitoringSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef);
    Task StopMonitoringAsync(IJSObjectReference monitorController);
    
    // Purge with progress (for future use if needed)
    Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false);
    Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false);
}
