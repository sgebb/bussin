using Bussin.Models;

namespace Bussin.Services;

public sealed class ServiceBusOperationsService : IServiceBusOperationsService
{
    private readonly IServiceBusJsInteropService _jsInterop;
    private readonly INotificationService _notificationService;
    private readonly ServiceBusTokenProvider _tokenProvider;

    public ServiceBusOperationsService(
        IServiceBusJsInteropService jsInterop,
        INotificationService notificationService,
        ServiceBusTokenProvider tokenProvider)
    {
        _jsInterop = jsInterop;
        _notificationService = notificationService;
        _tokenProvider = tokenProvider;
    }

    private static string EntityPath(string? queueName, string? topicName, string? subscriptionName, bool fromDeadLetter)
    {
        var path = queueName ?? $"{topicName}/subscriptions/{subscriptionName}";
        return fromDeadLetter ? $"{path}/$DeadLetterQueue" : path;
    }

    // Send operations

    public async Task SendQueueMessageAsync(string namespaceName, string queueName, object messageBody, MessageProperties? properties = null)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, queueName);
            await _jsInterop.SendQueueMessageAsync(namespaceName, queueName, token, messageBody, properties);
            _notificationService.NotifySuccess($"Message sent successfully to queue '{queueName}'");
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
            var token = await _tokenProvider.GetTokenAsync(namespaceName, topicName);
            await _jsInterop.SendTopicMessageAsync(namespaceName, topicName, token, messageBody, properties);
            _notificationService.NotifySuccess($"Message sent successfully to topic '{topicName}'");
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to send message: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> SendQueueMessagesBatchAsync(string namespaceName, string queueName, List<object> messages)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, queueName);
            await _jsInterop.SendQueueMessageBatchAsync(namespaceName, queueName, token, messages.ToArray());
            _notificationService.NotifySuccess($"Batch of {messages.Count} messages sent successfully to queue '{queueName}'");
            return new BatchOperationResult { SuccessCount = messages.Count };
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to send batch: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> SendTopicMessagesBatchAsync(string namespaceName, string topicName, List<object> messages)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, topicName);
            await _jsInterop.SendTopicMessageBatchAsync(namespaceName, topicName, token, messages.ToArray());
            _notificationService.NotifySuccess($"Batch of {messages.Count} messages sent successfully to topic '{topicName}'");
            return new BatchOperationResult { SuccessCount = messages.Count };
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to send batch: {ex.Message}");
            throw;
        }
    }

    // Lock settlement operations (used by ReceiveAndLockModal)

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

    // Operations on specific messages. Service Bus only receives deferred messages by sequence number,
    // so client-js locks from the head of the entity up to the requested messages, settles those and
    // releases the rest.

    public Task<BatchOperationResult> DeleteQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null)
        => DeleteMessagesAsync(namespaceName, queueName, null, null, sequenceNumbers, fromDeadLetter, sessionId);

    public Task<BatchOperationResult> DeleteSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null)
        => DeleteMessagesAsync(namespaceName, null, topicName, subscriptionName, sequenceNumbers, fromDeadLetter, sessionId);

    private async Task<BatchOperationResult> DeleteMessagesAsync(string namespaceName, string? queueName, string? topicName, string? subscriptionName, long[] sequenceNumbers, bool fromDeadLetter, string? sessionId)
    {
        try
        {
            return (await RemoveMessagesAsync(namespaceName, queueName, topicName, subscriptionName, sequenceNumbers, fromDeadLetter, sessionId)).ToBatchResult();
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to delete messages: {ex.Message}");
            throw;
        }
    }

    private async Task<SettleBySequenceResult> RemoveMessagesAsync(string namespaceName, string? queueName, string? topicName, string? subscriptionName, long[] sequenceNumbers, bool fromDeadLetter, string? sessionId)
    {
        var token = await _tokenProvider.GetTokenAsync(namespaceName, EntityPath(queueName, topicName, subscriptionName, fromDeadLetter));
        // A dead-letter queue never requires sessions
        var lockSessionId = fromDeadLetter ? null : sessionId;
        return queueName != null
            ? await _jsInterop.DeleteQueueMessagesBySequenceAsync(namespaceName, queueName, token, sequenceNumbers, fromDeadLetter, lockSessionId)
            : await _jsInterop.DeleteSubscriptionMessagesBySequenceAsync(namespaceName, topicName!, subscriptionName!, token, sequenceNumbers, fromDeadLetter, lockSessionId);
    }

    public Task<BatchOperationResult> MoveToDLQQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, string? reason = null, string? description = null, string? sessionId = null)
        => MoveToDLQAsync(namespaceName, queueName, null, null, sequenceNumbers, reason, description, sessionId);

    public Task<BatchOperationResult> MoveToDLQSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, string? reason = null, string? description = null, string? sessionId = null)
        => MoveToDLQAsync(namespaceName, null, topicName, subscriptionName, sequenceNumbers, reason, description, sessionId);

    private async Task<BatchOperationResult> MoveToDLQAsync(string namespaceName, string? queueName, string? topicName, string? subscriptionName, long[] sequenceNumbers, string? reason, string? description, string? sessionId)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, EntityPath(queueName, topicName, subscriptionName, false));
            reason ??= "Manual move to DLQ";
            description ??= "Moved by user";
            var result = queueName != null
                ? await _jsInterop.DeadLetterQueueMessagesBySequenceAsync(namespaceName, queueName, token, sequenceNumbers, reason, description, sessionId)
                : await _jsInterop.DeadLetterSubscriptionMessagesBySequenceAsync(namespaceName, topicName!, subscriptionName!, token, sequenceNumbers, reason, description, sessionId);
            return result.ToBatchResult();
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to move messages to DLQ: {ex.Message}");
            throw;
        }
    }

    public Task<BatchOperationResult> ResendQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true, string? sessionId = null)
        => ResendMessagesAsync(namespaceName, queueName, null, null, sequenceNumbers, fromDeadLetter, deleteOriginal, sessionId);

    public Task<BatchOperationResult> ResendSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, long[] sequenceNumbers, bool fromDeadLetter = false, bool deleteOriginal = true, string? sessionId = null)
        => ResendMessagesAsync(namespaceName, null, topicName, subscriptionName, sequenceNumbers, fromDeadLetter, deleteOriginal, sessionId);

    private async Task<BatchOperationResult> ResendMessagesAsync(string namespaceName, string? queueName, string? topicName, string? subscriptionName, long[] sequenceNumbers, bool fromDeadLetter, bool deleteOriginal, string? sessionId)
    {
        try
        {
            var sourceToken = await _tokenProvider.GetTokenAsync(namespaceName, EntityPath(queueName, topicName, subscriptionName, fromDeadLetter));
            var originals = queueName != null
                ? await _jsInterop.PeekQueueMessagesBySequenceAsync(namespaceName, queueName, sourceToken, sequenceNumbers, fromDeadLetter)
                : await _jsInterop.PeekSubscriptionMessagesBySequenceAsync(namespaceName, topicName!, subscriptionName!, sourceToken, sequenceNumbers, fromDeadLetter);

            if (originals.Count == 0)
            {
                return new SettleBySequenceResult { NotFound = sequenceNumbers }.ToBatchResult();
            }

            var batch = originals.Select(msg => new
            {
                body = msg.Body ?? "",
                properties = CreateResendProperties(msg, fromDeadLetter)
            }).ToArray<object>();

            // Messages go back to the queue, or to the topic the subscription belongs to
            var sendToken = await _tokenProvider.GetTokenAsync(namespaceName, queueName ?? topicName!);
            if (queueName != null)
            {
                await _jsInterop.SendQueueMessageBatchAsync(namespaceName, queueName, sendToken, batch);
            }
            else
            {
                await _jsInterop.SendTopicMessageBatchAsync(namespaceName, topicName!, sendToken, batch);
            }

            var result = new BatchOperationResult { SuccessCount = originals.Count };
            if (deleteOriginal)
            {
                var resent = originals.Select(m => m.SequenceNumber!.Value).ToArray();
                var removal = await RemoveMessagesAsync(namespaceName, queueName, topicName, subscriptionName, resent, fromDeadLetter, sessionId);
                result.FailureCount = removal.NotFound.Length;
                result.Errors = removal.NotFound.Select(seq => new BatchOperationError
                {
                    MessageId = $"#{seq}",
                    Error = "Resent, but the original could not be removed"
                }).ToList();
            }
            return result;
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to resend messages: {ex.Message}");
            throw;
        }
    }

    private static MessageProperties CreateResendProperties(ServiceBusMessage msg, bool fromDeadLetter)
    {
        // Start with existing application properties or create new dictionary
        var appProps = msg.ApplicationProperties != null
            ? new Dictionary<string, object>(msg.ApplicationProperties)
            : new Dictionary<string, object>();

        // Add resubmit marker
        appProps["x-bussin-resubmitted"] = "true";
        appProps["x-bussin-resubmitted-at"] = DateTime.UtcNow.ToString("o");
        if (fromDeadLetter && !string.IsNullOrEmpty(msg.MessageId))
        {
            appProps["x-bussin-original-message-id"] = msg.MessageId;
        }

        return new MessageProperties
        {
            MessageId = fromDeadLetter ? Guid.NewGuid().ToString() : msg.MessageId,
            ContentType = msg.ContentType,
            CorrelationId = msg.CorrelationId,
            Subject = msg.Subject,
            To = msg.To,
            ReplyTo = msg.ReplyTo,
            SessionId = msg.SessionId,
            PartitionKey = msg.PartitionKey,
            ApplicationProperties = appProps,
            // Carry over annotations if any (e.g. scheduled time)
            // But exclude standard ones that the broker will re-set (seq, enqueued time)
            MessageAnnotations = msg.MessageAnnotations != null
                ? msg.MessageAnnotations
                    .Where(k => !k.Key.StartsWith("x-opt-"))
                    .ToDictionary(k => k.Key, v => v.Value)
                : null
        };
    }

    public async Task<BatchOperationResult> CancelScheduledQueueMessagesAsync(string namespaceName, string queueName, long[] sequenceNumbers)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, queueName);
            await _jsInterop.CancelScheduledQueueMessagesAsync(namespaceName, queueName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Cancelled {sequenceNumbers.Length} scheduled message(s) from queue '{queueName}' successfully.");
            return new BatchOperationResult { SuccessCount = sequenceNumbers.Length };
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to cancel scheduled messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> CancelScheduledTopicMessagesAsync(string namespaceName, string topicName, long[] sequenceNumbers)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(namespaceName, topicName);
            await _jsInterop.CancelScheduledTopicMessagesAsync(namespaceName, topicName, token, sequenceNumbers);
            _notificationService.NotifySuccess($"Cancelled {sequenceNumbers.Length} scheduled message(s) from topic '{topicName}' successfully.");
            return new BatchOperationResult { SuccessCount = sequenceNumbers.Length };
        }
        catch (Exception ex)
        {
            _notificationService.NotifyError($"Failed to cancel scheduled messages: {ex.Message}");
            throw;
        }
    }
}
