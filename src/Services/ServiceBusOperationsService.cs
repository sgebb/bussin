using Bussin.Models;

namespace Bussin.Services;

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
            Console.WriteLine($"ERROR: {errorMsg}");
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

    // Internal lock-based operations
    
    private async Task<List<ServiceBusMessage>> LockQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            // Peek first to find the position of our target messages
            // This tells us how many messages are ahead of our targets
            var peeked = await _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, 100, 0, fromDeadLetter);
            
            // Find the index of the last message we need
            var maxSeq = sequenceNumbers.Max();
            var lastTargetIndex = peeked.FindLastIndex(m => m.SequenceNumber == maxSeq);
            
            // Lock up to and including that position (plus a small buffer)
            var countToLock = lastTargetIndex >= 0 ? lastTargetIndex + 1 : sequenceNumbers.Length;
            var lockedMessages = await _jsInterop.ReceiveAndLockQueueMessagesAsync(namespaceName, queueName, token, 5, fromDeadLetter, countToLock);
            return lockedMessages;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to lock messages: {ex.Message}");
            throw;
        }
    }

    private async Task<List<ServiceBusMessage>> LockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false)
    {
        try
        {
            var token = await GetTokenAsync();
            // Peek first to find the position of our target messages
            var peeked = await _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 100, 0, fromDeadLetter);
            
            // Find the index of the last message we need
            var maxSeq = sequenceNumbers.Max();
            var lastTargetIndex = peeked.FindLastIndex(m => m.SequenceNumber == maxSeq);
            
            // Lock up to and including that position
            var countToLock = lastTargetIndex >= 0 ? lastTargetIndex + 1 : sequenceNumbers.Length;
            var lockedMessages = await _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, token, 5, fromDeadLetter, countToLock);
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
        // Note: receive-by-sequence-number only works for DEFERRED messages.
        // So we use lock-match-complete approach.
        try
        {
            var lockedMessages = await LockQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, fromDeadLetter);
            
            var matchingTokens = lockedMessages
                .Where(m => m.LockToken != null && sequenceNumbers.Contains(m.SequenceNumber ?? -1))
                .Select(m => m.LockToken!)
                .ToArray();
            
            var result = await CompleteMessagesAsync(matchingTokens);
            
            // Abandon any locked messages we didn't use
            var unusedTokens = lockedMessages
                .Where(m => m.LockToken != null && !matchingTokens.Contains(m.LockToken))
                .Select(m => m.LockToken!)
                .ToArray();
            if (unusedTokens.Length > 0)
            {
                await AbandonMessagesAsync(unusedTokens);
            }
            
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
        // Note: receive-by-sequence-number only works for DEFERRED messages.
        // So we use lock-match-complete approach.
        try
        {
            var lockedMessages = await LockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, sequenceNumbers, fromDeadLetter);
            
            var matchingTokens = lockedMessages
                .Where(m => m.LockToken != null && sequenceNumbers.Contains(m.SequenceNumber ?? -1))
                .Select(m => m.LockToken!)
                .ToArray();
            
            var result = await CompleteMessagesAsync(matchingTokens);
            
            // Abandon any locked messages we didn't use
            var unusedTokens = lockedMessages
                .Where(m => m.LockToken != null && !matchingTokens.Contains(m.LockToken))
                .Select(m => m.LockToken!)
                .ToArray();
            if (unusedTokens.Length > 0)
            {
                await AbandonMessagesAsync(unusedTokens);
            }
            
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
            namespaceName,
            queueName,
            null, null,
            sequenceNumbers,
            fromDeadLetter,
            deleteOriginal
        );
    }

    public Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true)
    {
        return ResendMessagesInternalAsync(
            namespaceName,
            null,
            topicName, subscriptionName,
            sequenceNumbers,
            fromDeadLetter,
            deleteOriginal
        );
    }

    private async Task<BatchOperationResult> ResendMessagesInternalAsync(
        string namespaceName,
        string? queueName,
        string? topicName, string? subscriptionName,
        long[] sequenceNumbers,
        bool fromDeadLetter,
        bool deleteOriginal)
    {
        // Note: receive-by-sequence-number only works for DEFERRED messages.
        // So we use: peek (for content) + batch send + lock-match-complete (for deletion)
        try
        {
            var token = await GetTokenAsync();
            
            // Peek to get the message content we want to resend
            List<ServiceBusMessage> allMessages;
            if (queueName != null)
            {
                allMessages = await _jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, 100, 0, fromDeadLetter);
            }
            else
            {
                allMessages = await _jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, token, 100, 0, fromDeadLetter);
            }
            
            var messagesToResend = allMessages.Where(m => sequenceNumbers.Contains(m.SequenceNumber ?? -1)).ToList();
            
            if (!messagesToResend.Any())
            {
                return new BatchOperationResult { SuccessCount = 0, FailureCount = sequenceNumbers.Length, Errors = [] };
            }
            
            // Prepare batch of messages to send
            var batchMessages = messagesToResend.Select(msg => new
            {
                body = msg.Body ?? "",
                properties = CreateResendProperties(msg)
            }).ToArray<object>();
            
            // Send all messages in one batch (single connection)
            if (queueName != null)
            {
                await _jsInterop.SendQueueMessageBatchAsync(namespaceName, queueName, token, batchMessages);
            }
            else
            {
                await _jsInterop.SendTopicMessageBatchAsync(namespaceName, topicName!, token, batchMessages);
            }
            
            // Delete originals using lock-match-complete (since receive-by-sequence only works for deferred)
            if (deleteOriginal)
            {
                List<ServiceBusMessage> lockedMessages;
                if (queueName != null)
                {
                    lockedMessages = await LockQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, fromDeadLetter);
                }
                else
                {
                    lockedMessages = await LockSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, sequenceNumbers, fromDeadLetter);
                }
                
                // Match by sequence number and complete only those
                var matchingTokens = lockedMessages
                    .Where(m => m.LockToken != null && sequenceNumbers.Contains(m.SequenceNumber ?? -1))
                    .Select(m => m.LockToken!)
                    .ToArray();
                
                if (matchingTokens.Length > 0)
                {
                    await CompleteMessagesAsync(matchingTokens);
                }
                
                // Abandon any locked messages we didn't use
                var unusedTokens = lockedMessages
                    .Where(m => m.LockToken != null && !matchingTokens.Contains(m.LockToken))
                    .Select(m => m.LockToken!)
                    .ToArray();
                if (unusedTokens.Length > 0)
                {
                    await AbandonMessagesAsync(unusedTokens);
                }
            }
            
            return new BatchOperationResult { SuccessCount = messagesToResend.Count, FailureCount = 0, Errors = [] };
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
        // Always use the decoded body approach - don't try to preserve original binary format
        // The OriginalBody is a raw AMQP structure (JsonElement) that doesn't serialize correctly through Blazor JS interop
        return new MessageProperties
        {
            MessageId = msg.MessageId,
            ContentType = msg.ContentType,
            ApplicationProperties = msg.ApplicationProperties
        };
    }

    public async Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        // Note: receive-by-sequence-number only works for DEFERRED messages, not regular messages.
        // So we must use the lock-match-abandon approach for moving to DLQ.
        try
        {
            // Lock more messages than needed to ensure we get the ones we want
            var lockedMessages = await LockQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, false);
            
            // Match by sequence number and dead letter only those
            var matchingTokens = lockedMessages
                .Where(m => m.LockToken != null && sequenceNumbers.Contains(m.SequenceNumber ?? -1))
                .Select(m => m.LockToken!)
                .ToArray();
            
            var result = await DeadLetterMessagesAsync(matchingTokens, new DeadLetterOptions 
            { 
                DeadLetterReason = "Manual move to DLQ", 
                DeadLetterErrorDescription = "Moved by user" 
            });
            
            // Abandon any locked messages we didn't use
            var unusedTokens = lockedMessages
                .Where(m => m.LockToken != null && !matchingTokens.Contains(m.LockToken))
                .Select(m => m.LockToken!)
                .ToArray();
            if (unusedTokens.Length > 0)
            {
                await AbandonMessagesAsync(unusedTokens);
            }
            
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
        // Note: receive-by-sequence-number only works for DEFERRED messages, not regular messages.
        // So we must use the lock-match-abandon approach for moving to DLQ.
        try
        {
            // Lock more messages than needed to ensure we get the ones we want
            var lockedMessages = await LockSubscriptionMessagesAsync(namespaceName, topicName, subscriptionName, sequenceNumbers, false);
            
            // Match by sequence number and dead letter only those
            var matchingTokens = lockedMessages
                .Where(m => m.LockToken != null && sequenceNumbers.Contains(m.SequenceNumber ?? -1))
                .Select(m => m.LockToken!)
                .ToArray();
            
            var result = await DeadLetterMessagesAsync(matchingTokens, new DeadLetterOptions 
            { 
                DeadLetterReason = "Manual move to DLQ", 
                DeadLetterErrorDescription = "Moved by user" 
            });
            
            // Abandon any locked messages we didn't use
            var unusedTokens = lockedMessages
                .Where(m => m.LockToken != null && !matchingTokens.Contains(m.LockToken))
                .Select(m => m.LockToken!)
                .ToArray();
            if (unusedTokens.Length > 0)
            {
                await AbandonMessagesAsync(unusedTokens);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to move messages to DLQ: {ex.Message}");
            throw;
        }
    }
}
