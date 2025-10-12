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
    
    public async Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, int count = 10, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, count, 0, fromDeadLetter);
            var source = fromDeadLetter ? "DLQ" : "queue";
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from {source}");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek messages: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, int count = 10, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var messages = await _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, count, 0, fromDeadLetter);
            var source = fromDeadLetter ? "DLQ" : "subscription";
            _notificationService.NotifySuccess($"Peeked {messages.Count} messages from {source}");
            return messages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to peek messages: {ex.Message}");
            throw;
        }
    }

    // Send operations
    
    public async Task SendQueueMessageAsync(string namespaceName, string queueName, object messageBody, MessageProperties? properties = null)
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

    public async Task SendTopicMessageAsync(string namespaceName, string topicName, object messageBody, MessageProperties? properties = null)
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
    
    public async Task<int> PurgeQueueAsync(string namespaceName, string queueName, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var count = await _jsInterop.PurgeQueueAsync(namespaceName, queueName, token, fromDeadLetter);
            var source = fromDeadLetter ? "DLQ" : "queue";
            _notificationService.NotifySuccess($"{source} purged successfully");
            return count;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to purge: {ex.Message}");
            throw;
        }
    }

    public async Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var count = await _jsInterop.PurgeSubscriptionAsync(namespaceName, topicName, subscriptionName, token, fromDeadLetter);
            var source = fromDeadLetter ? "DLQ" : "subscription";
            _notificationService.NotifySuccess($"{source} purged successfully");
            return count;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to purge: {ex.Message}");
            throw;
        }
    }

    // Lock-based batch operations (new workflow: lock -> settle)
    
    public async Task<List<ServiceBusMessage>> LockQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var lockedMessages = await _jsInterop.ReceiveAndLockQueueMessagesAsync(namespaceName, queueName, token, 5, fromDeadLetter, sequenceNumbers.Length);
            return lockedMessages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to lock messages: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ServiceBusMessage>> LockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            var lockedMessages = await _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 5, fromDeadLetter, sequenceNumbers.Length);
            return lockedMessages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to lock messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens)
    {
        try
        {
            var result = await _jsInterop.CompleteMessagesAsync(lockTokens);
            _notificationService.NotifySuccess($"Completed {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to complete messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
    {
        try
        {
            var result = await _jsInterop.AbandonMessagesAsync(lockTokens);
            _notificationService.NotifySuccess($"Abandoned {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to abandon messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null)
    {
        try
        {
            var result = await _jsInterop.DeadLetterMessagesAsync(lockTokens, options);
            _notificationService.NotifySuccess($"Dead lettered {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to dead letter messages: {ex.Message}");
            throw;
        }
    }

    // High-level operations (lock + settle in one call)
    
    public async Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            // Lock messages
            var lockedMessages = await LockQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, fromDeadLetter);
            
            // Complete (delete) them
            var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
            var result = await CompleteMessagesAsync(lockTokens);
            
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to delete messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            // Lock messages
            var lockedMessages = await LockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, sequenceNumbers, fromDeadLetter);
            
            // Complete (delete) them
            var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
            var result = await CompleteMessagesAsync(lockTokens);
            
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to delete messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            
            // First, peek the messages from DLQ to get their content
            var allMessages = await _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, 100, 0, fromDeadLetter);
            var messagesToResend = allMessages.Where(m => sequenceNumbers.Contains(m.SequenceNumber ?? -1)).ToList();
            
            if (!messagesToResend.Any())
            {
                return new BatchOperationResult { SuccessCount = 0, FailureCount = sequenceNumbers.Length, Errors = new List<BatchOperationError>() };
            }
            
            // Lock the messages from DLQ (receive and lock to get lock tokens)
            var lockedMessages = await _jsInterop.ReceiveAndLockQueueMessagesAsync(namespaceName, queueName, token, 5, fromDeadLetter, messagesToResend.Count);
            
            // Send them back to the main queue
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<BatchOperationError>();
            
            foreach (var msg in lockedMessages)
            {
                try
                {
                    // Preserve original message properties
                    var props = new MessageProperties
                    {
                        MessageId = msg.MessageId,
                        ContentType = msg.ContentType,
                        ApplicationProperties = msg.ApplicationProperties
                    };
                    
                    await _jsInterop.SendQueueMessageAsync(namespaceName, queueName, token, msg.Body, props);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    errors.Add(new BatchOperationError { MessageId = msg.MessageId ?? "", Error = ex.Message });
                    Console.WriteLine($"Failed to resend message {msg.MessageId}: {ex.Message}");
                }
            }
            
            // Complete (delete) the DLQ messages that were successfully sent
            if (lockedMessages.Any())
            {
                var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
                await _jsInterop.CompleteMessagesAsync(lockTokens);
            }
            
            var result = new BatchOperationResult { SuccessCount = successCount, FailureCount = failureCount, Errors = errors };
            _notificationService.NotifySuccess($"Resent {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            Console.WriteLine($"Resend error details: {ex}");
            throw;
        }
    }

    public async Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            
            // First, peek the messages from DLQ to get their content
            var allMessages = await _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 100, 0, fromDeadLetter);
            var messagesToResend = allMessages.Where(m => sequenceNumbers.Contains(m.SequenceNumber ?? -1)).ToList();
            
            if (!messagesToResend.Any())
            {
                return new BatchOperationResult { SuccessCount = 0, FailureCount = sequenceNumbers.Length, Errors = new List<BatchOperationError>() };
            }
            
            // Lock the messages from DLQ (receive and lock to get lock tokens)
            var lockedMessages = await _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 5, fromDeadLetter, messagesToResend.Count);
            
            // Send them back to the topic
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<BatchOperationError>();
            
            foreach (var msg in lockedMessages)
            {
                try
                {
                    // Preserve original message properties
                    var props = new MessageProperties
                    {
                        MessageId = msg.MessageId,
                        ContentType = msg.ContentType,
                        ApplicationProperties = msg.ApplicationProperties
                    };
                    
                    await _jsInterop.SendTopicMessageAsync(namespaceName, topicName, token, msg.Body, props);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    errors.Add(new BatchOperationError { MessageId = msg.MessageId ?? "", Error = ex.Message });
                    Console.WriteLine($"Failed to resend message {msg.MessageId}: {ex.Message}");
                }
            }
            
            // Complete (delete) the DLQ messages that were successfully sent
            if (lockedMessages.Any())
            {
                var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
                await _jsInterop.CompleteMessagesAsync(lockTokens);
            }
            
            var result = new BatchOperationResult { SuccessCount = successCount, FailureCount = failureCount, Errors = errors };
            _notificationService.NotifySuccess($"Resent {result.SuccessCount} messages ({result.FailureCount} failed)");
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            Console.WriteLine($"Resend error details: {ex}");
            throw;
        }
    }

    public async Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            // Lock messages from main queue
            var lockedMessages = await LockQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, false);
            
            // Dead letter them
            var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
            var result = await DeadLetterMessagesAsync(lockTokens, new DeadLetterOptions 
            { 
                DeadLetterReason = "Manual move to DLQ", 
                DeadLetterErrorDescription = "Moved by user" 
            });
            
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
            // Lock messages from main subscription
            var lockedMessages = await LockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, sequenceNumbers, false);
            
            // Dead letter them
            var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
            var result = await DeadLetterMessagesAsync(lockTokens, new DeadLetterOptions 
            { 
                DeadLetterReason = "Manual move to DLQ", 
                DeadLetterErrorDescription = "Moved by user" 
            });
            
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to move messages to DLQ: {ex.Message}");
            throw;
        }
    }
}
