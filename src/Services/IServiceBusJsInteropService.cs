using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

public interface IServiceBusJsInteropService
{
    // Queue Operations
    Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, long fromSequence = 0, bool fromDeadLetter = false, string? sessionId = null);
    Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null);

    // Topic/Subscription Operations
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, long fromSequence = 0, bool fromDeadLetter = false, string? sessionId = null);
    Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null);

    // Batch send operations
    Task SendQueueMessageBatchAsync(string namespaceName, string queueName, string token, object[] messages);
    Task SendTopicMessageBatchAsync(string namespaceName, string topicName, string token, object[] messages);

    // Lock-based operations
    Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1, string? sessionId = null);
    Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1, string? sessionId = null);
    Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null);

    // Monitor operations (continuous non-destructive)
    Task<IJSObjectReference> StartMonitoringAsync(string namespaceName, string entityPath, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef);
    Task StopMonitoringAsync(IJSObjectReference monitorController);

    // Long-running operations return a controller; AwaitControllerAsync waits for its result
    Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false, bool requiresSession = false);
    Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false, bool requiresSession = false);
    Task<IJSObjectReference> StartSearchQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50);
    Task<IJSObjectReference> StartSearchSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50);
    Task<T> AwaitControllerAsync<T>(IJSObjectReference controller);

    // Delete or dead-letter specific messages (locks from the head of the entity up to them)
    Task<SettleBySequenceResult> DeleteQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null);
    Task<SettleBySequenceResult> DeleteSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null);
    Task<SettleBySequenceResult> DeadLetterQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, string reason, string description, string? sessionId = null);
    Task<SettleBySequenceResult> DeadLetterSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, string reason, string description, string? sessionId = null);

    // Cancel scheduled messages by sequence numbers (direct, no lock needed)
    Task CancelScheduledQueueMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers);
    Task CancelScheduledTopicMessagesAsync(string namespaceName, string topicName, string token, long[] sequenceNumbers);

    // Peek specific messages by sequence numbers
    Task<List<ServiceBusMessage>> PeekQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false);
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false);

    // Rule Operations
    Task<List<SubscriptionRule>> EnumerateRulesAsync(string namespaceName, string topicName, string subscriptionName, string token);
    Task AddRuleAsync(string namespaceName, string topicName, string subscriptionName, string token, string ruleName, string filterType, object filterExpression, string? action);
    Task RemoveRuleAsync(string namespaceName, string topicName, string subscriptionName, string token, string ruleName);
}
