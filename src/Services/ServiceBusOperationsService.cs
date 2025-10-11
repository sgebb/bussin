using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class ServiceBusOperationsService : IServiceBusOperationsService
{
    private readonly IServiceBusJsInteropService _jsInterop;
    private readonly IAuthenticationService _authService;
    private readonly INotificationService _notificationService;

    public ServiceBusOperationsService(
        IServiceBusJsInteropService jsInterop,
        IAuthenticationService authService,
        INotificationService notificationService)
    {
        _jsInterop = jsInterop;
        _authService = authService;
        _notificationService = notificationService;
    }

    private async Task<string> GetTokenAsync()
    {
        var token = await _authService.GetServiceBusTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            var errorMsg = "Service Bus token not available. You may need to sign out and sign in again to consent to the Service Bus scope.";
            Console.WriteLine($"✗ {errorMsg}");
            throw new InvalidOperationException(errorMsg);
        }
        return token;
    }

    // Peek operations
    
    public async Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, int count = 10)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, count);
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from queue");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek messages: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, count);
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from subscription");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek messages: {ex.Message}");
            throw;
        }
    }

    // Dead Letter Queue peek operations
    
    public async Task<List<ServiceBusMessage>> PeekQueueDeadLetterMessagesAsync(string namespaceName, string queueName, int count = 10)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekQueueDeadLetterMessagesAsync(namespaceName, queueName, token, count);
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from queue DLQ");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek DLQ messages: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ServiceBusMessage>> PeekSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekSubscriptionDeadLetterMessagesAsync(namespaceName, topicName, subscriptionName, token, count);
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from subscription DLQ");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek DLQ messages: {ex.Message}");
            throw;
        }
    }

    // Send operations
    
    public async Task SendQueueMessageAsync(string namespaceName, string queueName, object messageBody, object? properties = null)
    {
        try
        {
            var token = await GetTokenAsync();
            await _jsInterop.SendQueueMessageAsync(namespaceName, queueName, token, messageBody, properties);
            _notificationService.NotifySuccess("Message sent successfully");
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to send message: {ex.Message}");
            throw;
        }
    }

    public async Task SendTopicMessageAsync(string namespaceName, string topicName, object messageBody, object? properties = null)
    {
        try
        {
            var token = await GetTokenAsync();
            await _jsInterop.SendTopicMessageAsync(namespaceName, topicName, token, messageBody, properties);
            _notificationService.NotifySuccess("Message sent successfully");
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to send message: {ex.Message}");
            throw;
        }
    }

    // Purge operations
    
    public async Task<int> PurgeQueueAsync(string namespaceName, string queueName)
    {
        try
        {
            var token = await GetTokenAsync();
            var count = await _jsInterop.PurgeQueueAsync(namespaceName, queueName, token);
            _notificationService.NotifySuccess("Queue purged successfully");
            return count;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to purge queue: {ex.Message}");
            throw;
        }
    }

    public async Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName)
    {
        try
        {
            var token = await GetTokenAsync();
            var count = await _jsInterop.PurgeSubscriptionAsync(namespaceName, topicName, subscriptionName, token);
            _notificationService.NotifySuccess("Subscription purged successfully");
            return count;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to purge subscription: {ex.Message}");
            throw;
        }
    }

    // Batch operations
    
    public async Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.DeleteQueueMessagesAsync(namespaceName, queueName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Deleted {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to delete messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.DeleteSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Deleted {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to delete messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.ResendQueueMessagesAsync(namespaceName, queueName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Resent {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.ResendSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Resent {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.MoveToDLQQueueMessagesAsync(namespaceName, queueName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Moved {result.SuccessCount} messages to DLQ ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to move messages to DLQ: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> MoveToDLQSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.MoveToDLQSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Moved {result.SuccessCount} messages to DLQ ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to move messages to DLQ: {ex.Message}");
            throw;
        }
    }

    // Dead Letter Queue resubmit operations
    
    public async Task<BatchOperationResult> ResubmitQueueDeadLetterMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.ResubmitQueueDeadLetterMessagesAsync(namespaceName, queueName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Resubmitted {result.SuccessCount} messages from DLQ ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resubmit messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> ResubmitSubscriptionDeadLetterMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers)
    {
        try
        {
            var token = await GetTokenAsync();
            var result = await _jsInterop.ResubmitSubscriptionDeadLetterMessagesAsync(namespaceName, topicName, subscriptionName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Resubmitted {result.SuccessCount} messages from DLQ ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resubmit messages: {ex.Message}");
            throw;
        }
    }
}
