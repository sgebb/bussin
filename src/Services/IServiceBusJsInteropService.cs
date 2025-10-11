using Microsoft.JSInterop;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public interface IServiceBusJsInteropService
{
    // Queue Operations
    Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0);
    Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, object? properties = null);
    Task<int> PurgeQueueAsync(string namespaceName, string queueName, string token);
    Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers);
    Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers);
    Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers);
    
    // Queue Dead Letter Operations
    Task<List<ServiceBusMessage>> PeekQueueDeadLetterMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0);
    Task<BatchOperationResult> ResubmitQueueDeadLetterMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers);
    
    // Topic/Subscription Operations
    Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0);
    Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, object? properties = null);
    Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token);
    Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers);
    Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers);
    Task<BatchOperationResult> MoveToDLQSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers);
    
    // Subscription Dead Letter Operations
    Task<List<ServiceBusMessage>> PeekSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0);
    Task<BatchOperationResult> ResubmitSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers);
}
