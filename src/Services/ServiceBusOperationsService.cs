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
            _notificationService.NotifySuccess($"{source} purged successfully - deleted {count} message(s)");
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
            _notificationService.NotifySuccess($"{source} purged successfully - deleted {count} message(s)");
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
            return await _jsInterop.CompleteMessagesAsync(lockTokens);
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
            return await _jsInterop.AbandonMessagesAsync(lockTokens);
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
            return await _jsInterop.DeadLetterMessagesAsync(lockTokens, options);
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

    public Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true)
    {
        return ResendMessagesInternalAsync(
            sequenceNumbers,
            fromDeadLetter,
            deleteOriginal,
            token => _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, 100, 0, fromDeadLetter),
            (token, count) => _jsInterop.ReceiveAndLockQueueMessagesAsync(namespaceName, queueName, token, 5, fromDeadLetter, count),
            (token, body, props) => _jsInterop.SendQueueMessageAsync(namespaceName, queueName, token, body, props)
        );
    }

    public Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true)
    {
        return ResendMessagesInternalAsync(
            sequenceNumbers,
            fromDeadLetter,
            deleteOriginal,
            token => _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 100, 0, fromDeadLetter),
            (token, count) => _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 5, fromDeadLetter, count),
            (token, body, props) => _jsInterop.SendTopicMessageAsync(namespaceName, topicName, token, body, props)
        );
    }

    private async Task<BatchOperationResult> ResendMessagesInternalAsync(
        long[] sequenceNumbers,
        bool fromDeadLetter,
        bool deleteOriginal,
        Func<string, Task<List<ServiceBusMessage>>> peekMessages,
        Func<string, int, Task<List<ServiceBusMessage>>> lockMessages,
        Func<string, object, MessageProperties?, Task> sendMessage)
    {
        try
        {
            var token = await GetTokenAsync();
            
            // Peek to get the message content we want to resend
            var allMessages = await peekMessages(token);
            var messagesToResend = allMessages.Where(m => sequenceNumbers.Contains(m.SequenceNumber ?? -1)).ToList();
            
            if (!messagesToResend.Any())
            {
                return new BatchOperationResult { SuccessCount = 0, FailureCount = sequenceNumbers.Length, Errors = [] };
            }
            
            // If we need to delete originals, lock the messages to get lock tokens
            // But we use the PEEKED messages for content (locked messages are matched by sequence number)
            List<ServiceBusMessage>? lockedMessages = null;
            if (deleteOriginal)
            {
                lockedMessages = await lockMessages(token, messagesToResend.Count);
            }
            
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<BatchOperationError>();
            
            // Use peeked messages for content - they have the correct body
            foreach (var msg in messagesToResend)
            {
                try
                {
                    var props = CreateResendProperties(msg);
                    // When OriginalBody exists, it's passed via props and body should be empty
                    // Otherwise use the regular Body
                    var body = msg.OriginalBody != null ? "" : msg.Body;
                    await sendMessage(token, body ?? "", props);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    errors.Add(new BatchOperationError { MessageId = msg.MessageId ?? "", Error = ex.Message });
                    Console.WriteLine($"Failed to resend message {msg.MessageId}: {ex.Message}");
                }
            }
            
            // Complete the locked messages to remove them from the queue
            if (deleteOriginal && lockedMessages != null && lockedMessages.Any())
            {
                var lockTokens = lockedMessages.Where(m => m.LockToken != null).Select(m => m.LockToken!).ToArray();
                if (lockTokens.Length > 0)
                {
                    await _jsInterop.CompleteMessagesAsync(lockTokens);
                }
            }
            
            return new BatchOperationResult { SuccessCount = successCount, FailureCount = failureCount, Errors = errors };
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            Console.WriteLine($"Resend error details: {ex}");
            throw;
        }
    }

    private static MessageProperties CreateResendProperties(ServiceBusMessage msg)
    {
        if (msg.OriginalBody != null && msg.OriginalContentType != null)
        {
            return new MessageProperties
            {
                MessageId = msg.MessageId,
                OriginalBody = msg.OriginalBody,
                OriginalContentType = msg.OriginalContentType,
                ApplicationProperties = msg.ApplicationProperties
            };
        }
        
        return new MessageProperties
        {
            MessageId = msg.MessageId,
            ContentType = msg.ContentType,
            ApplicationProperties = msg.ApplicationProperties
        };
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
