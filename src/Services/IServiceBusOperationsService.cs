using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

/// <summary>
/// High-level service that combines JS interop operations with notification support
/// </summary>
public interface IServiceBusOperationsService
{
    // Peek operations (non-destructive, read-only)
    Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, int count = 10, bool fromDeadLetter = false);
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10, bool fromDeadLetter = false);
    
    // Send operations
    Task SendQueueMessageAsync(string namespaceName, string queueName, object messageBody, MessageProperties? properties = null);
    Task SendTopicMessageAsync(string namespaceName, string topicName, object messageBody, MessageProperties? properties = null);
    
    // Purge operations
    Task<int> PurgeQueueAsync(string namespaceName, string queueName, bool fromDeadLetter = false);
    Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, bool fromDeadLetter = false);
    
    // Lock-based batch operations (new workflow: lock -> settle)
    Task<List<ServiceBusMessage>> LockQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false);
    Task<List<ServiceBusMessage>> LockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false);
    Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens);
    Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null);
    
    // High-level operations (lock + settle in one call)
    Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false);
    Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false);
    Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true);
    Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true);
    Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers);
    Task<BatchOperationResult> MoveToDLQSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers);
}
