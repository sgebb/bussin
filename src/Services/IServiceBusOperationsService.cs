using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

/// <summary>
/// High-level service that combines JS interop operations with notification support
/// </summary>
public interface IServiceBusOperationsService
{
    // Peek operations
    Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, int count = 10);
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10);
    
    // Dead Letter Queue peek operations
    Task<List<ServiceBusMessage>> PeekQueueDeadLetterMessagesAsync(string namespaceName, string queueName, int count = 10);
    Task<List<ServiceBusMessage>> PeekSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10);
    
    // Send operations
    Task SendQueueMessageAsync(string namespaceName, string queueName, object messageBody, object? properties = null);
    Task SendTopicMessageAsync(string namespaceName, string topicName, object messageBody, object? properties = null);
    
    // Purge operations
    Task<int> PurgeQueueAsync(string namespaceName, string queueName);
    Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName);
    
    // Batch operations
    Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers);
    Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers);
    Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers);
    Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers);
    Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers);
    Task<BatchOperationResult> MoveToDLQSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers);
    
    // Dead Letter Queue resubmit operations
    Task<BatchOperationResult> ResubmitQueueDeadLetterMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers);
    Task<BatchOperationResult> ResubmitSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers);
}
