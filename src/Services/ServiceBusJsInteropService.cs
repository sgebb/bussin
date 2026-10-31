using Microsoft.JSInterop;
using System.Text.Json;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Calls the client-js ServiceBusAPI (window.ServiceBusAPI) and the small glue in js/storage.js.
/// </summary>
public class ServiceBusJsInteropService(IJSRuntime jsRuntime) : IServiceBusJsInteropService
{
    protected IJSRuntime JSRuntime => jsRuntime;

    /// <summary>Runs before every call; the demo service enables and seeds the simulator here.</summary>
    protected virtual Task EnsureReadyAsync() => Task.CompletedTask;

    private async Task<T> CallAsync<T>(string identifier, params object?[] args)
    {
        await EnsureReadyAsync();
        return await jsRuntime.InvokeAsync<T>(identifier, args);
    }

    private async Task CallVoidAsync(string identifier, params object?[] args)
    {
        await EnsureReadyAsync();
        await jsRuntime.InvokeVoidAsync(identifier, args);
    }

    private async Task<List<ServiceBusMessage>> CallMessagesAsync(string identifier, params object?[] args)
    {
        var result = await CallAsync<JsonElement[]>(identifier, args);
        return result.Select(SafeDeserializeMessage).OfType<ServiceBusMessage>().ToList();
    }

    /// <summary>
    /// Deserializes a message; one with malformed fields still shows its body, id and sequence number.
    /// </summary>
    private static ServiceBusMessage? SafeDeserializeMessage(JsonElement elem)
    {
        try
        {
            return JsonSerializer.Deserialize<ServiceBusMessage>(elem.GetRawText());
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Showing a partial message, its properties could not be read: {ex.Message}");
            try
            {
                string bodyProp;
                if (elem.TryGetProperty("body", out var body))
                {
                    bodyProp = body.ValueKind == JsonValueKind.String ? (body.GetString() ?? "") : body.GetRawText();
                }
                else
                {
                    bodyProp = "[Unable to parse body]";
                }

                var msgIdProp = elem.TryGetProperty("messageId", out var msgId) && msgId.ValueKind == JsonValueKind.String ? msgId.GetString() : null;

                long? sequenceNumberProp = null;
                if (elem.TryGetProperty("sequenceNumber", out var seqNumProp))
                {
                    if (seqNumProp.ValueKind == JsonValueKind.Number)
                        sequenceNumberProp = seqNumProp.GetInt64();
                    else if (seqNumProp.ValueKind == JsonValueKind.String && long.TryParse(seqNumProp.GetString(), out var parsedSeq))
                        sequenceNumberProp = parsedSeq;
                }

                var sessionIdProp = elem.TryGetProperty("sessionId", out var sessIdProp) && sessIdProp.ValueKind == JsonValueKind.String ? sessIdProp.GetString() : null;
                var lockTokenProp = elem.TryGetProperty("lockToken", out var lockToken) && lockToken.ValueKind == JsonValueKind.String ? lockToken.GetString() : null;

                return new ServiceBusMessage
                {
                    MessageId = msgIdProp,
                    Body = bodyProp,
                    LockToken = lockTokenProp,
                    SequenceNumber = sequenceNumberProp,
                    SessionId = sessionIdProp
                };
            }
            catch
            {
                // If we can't even extract basic info, return null
                return null;
            }
        }
    }

    // Queue Operations

    public Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, long fromSequence = 0, bool fromDeadLetter = false, string? sessionId = null)
        => CallMessagesAsync("ServiceBusAPI.peekQueueMessages", namespaceName, queueName, token, count, fromSequence, fromDeadLetter, sessionId);

    public Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null)
        => CallVoidAsync("ServiceBusAPI.sendQueueMessage", namespaceName, queueName, token, messageBody, properties);

    // Topic/Subscription Operations

    public Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, long fromSequence = 0, bool fromDeadLetter = false, string? sessionId = null)
        => CallMessagesAsync("ServiceBusAPI.peekSubscriptionMessages", namespaceName, topicName, subscriptionName, token, count, fromSequence, fromDeadLetter, sessionId);

    public Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null)
        => CallVoidAsync("ServiceBusAPI.sendTopicMessage", namespaceName, topicName, token, messageBody, properties);

    // Batch send operations

    public Task SendQueueMessageBatchAsync(string namespaceName, string queueName, string token, object[] messages)
        => CallVoidAsync("ServiceBusAPI.sendQueueMessageBatch", namespaceName, queueName, token, messages);

    public Task SendTopicMessageBatchAsync(string namespaceName, string topicName, string token, object[] messages)
        => CallVoidAsync("ServiceBusAPI.sendTopicMessageBatch", namespaceName, topicName, token, messages);

    // Lock-based operations

    public Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1, string? sessionId = null)
        => CallMessagesAsync("ServiceBusAPI.receiveAndLockQueueMessage", namespaceName, queueName, token, timeoutSeconds, fromDeadLetter, count, sessionId);

    public Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1, string? sessionId = null)
        => CallMessagesAsync("ServiceBusAPI.receiveAndLockSubscriptionMessage", namespaceName, topicName, subscriptionName, token, timeoutSeconds, fromDeadLetter, count, sessionId);

    // A lone string[] would otherwise spread into the params array
    public Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens)
        => CallAsync<BatchOperationResult>("ServiceBusAPI.complete", (object)lockTokens);

    public Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
        => CallAsync<BatchOperationResult>("ServiceBusAPI.abandon", (object)lockTokens);

    public Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null)
        => CallAsync<BatchOperationResult>("ServiceBusAPI.deadLetter", lockTokens, options ?? new DeadLetterOptions());

    // Monitor operations (continuous non-destructive)

    public Task<IJSObjectReference> StartMonitoringAsync(string namespaceName, string entityPath, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
        => CallAsync<IJSObjectReference>("startMonitoring", namespaceName, entityPath, token, callbackRef);

    public async Task StopMonitoringAsync(IJSObjectReference monitorController)
        => await monitorController.InvokeVoidAsync("stop");

    // Long-running operations

    public Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false, bool requiresSession = false)
        => CallAsync<IJSObjectReference>("startPurge", callbackRef, namespaceName, queueName, null, token, fromDeadLetter, requiresSession);

    public Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false, bool requiresSession = false)
        => CallAsync<IJSObjectReference>("startPurge", callbackRef, namespaceName, topicName, subscriptionName, token, fromDeadLetter, requiresSession);

    public Task<IJSObjectReference> StartSearchQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
        => CallAsync<IJSObjectReference>("startSearch", callbackRef, namespaceName, queueName, null, token, fromDeadLetter,
            bodyFilter ?? "", messageIdFilter ?? "", subjectFilter ?? "", maxMessages, maxMatches);

    public Task<IJSObjectReference> StartSearchSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<SearchProgressCallback> callbackRef, bool fromDeadLetter, string? bodyFilter, string? messageIdFilter, string? subjectFilter, int maxMessages, int maxMatches = 50)
        => CallAsync<IJSObjectReference>("startSearch", callbackRef, namespaceName, topicName, subscriptionName, token, fromDeadLetter,
            bodyFilter ?? "", messageIdFilter ?? "", subjectFilter ?? "", maxMessages, maxMatches);

    public Task<T> AwaitControllerAsync<T>(IJSObjectReference controller)
        => CallAsync<T>("awaitControllerPromise", controller);

    // Delete or dead-letter specific messages

    public Task<SettleBySequenceResult> DeleteQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null)
        => CallAsync<SettleBySequenceResult>("ServiceBusAPI.deleteQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, fromDeadLetter, sessionId);

    public Task<SettleBySequenceResult> DeleteSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false, string? sessionId = null)
        => CallAsync<SettleBySequenceResult>("ServiceBusAPI.deleteSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, fromDeadLetter, sessionId);

    public Task<SettleBySequenceResult> DeadLetterQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, string reason, string description, string? sessionId = null)
        => CallAsync<SettleBySequenceResult>("ServiceBusAPI.deadLetterQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, reason, description, sessionId);

    public Task<SettleBySequenceResult> DeadLetterSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, string reason, string description, string? sessionId = null)
        => CallAsync<SettleBySequenceResult>("ServiceBusAPI.deadLetterSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, reason, description, sessionId);

    // Cancel scheduled messages

    public Task CancelScheduledQueueMessagesAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers)
        => CallVoidAsync("ServiceBusAPI.cancelScheduledQueueMessages", namespaceName, queueName, token, sequenceNumbers);

    public Task CancelScheduledTopicMessagesAsync(string namespaceName, string topicName, string token, long[] sequenceNumbers)
        => CallVoidAsync("ServiceBusAPI.cancelScheduledTopicMessages", namespaceName, topicName, token, sequenceNumbers);

    // Peek specific messages by sequence numbers

    public Task<List<ServiceBusMessage>> PeekQueueMessagesBySequenceAsync(string namespaceName, string queueName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => CallMessagesAsync("ServiceBusAPI.peekQueueMessagesBySequence", namespaceName, queueName, token, sequenceNumbers, fromDeadLetter);

    public Task<List<ServiceBusMessage>> PeekSubscriptionMessagesBySequenceAsync(string namespaceName, string topicName, string subscriptionName, string token, long[] sequenceNumbers, bool fromDeadLetter = false)
        => CallMessagesAsync("ServiceBusAPI.peekSubscriptionMessagesBySequence", namespaceName, topicName, subscriptionName, token, sequenceNumbers, fromDeadLetter);

    // Rule Operations

    public async Task<List<SubscriptionRule>> EnumerateRulesAsync(string namespaceName, string topicName, string subscriptionName, string token)
    {
        var result = await CallAsync<JsonElement[]>("ServiceBusAPI.enumerateRules", namespaceName, topicName, subscriptionName, token);
        return result.Select(elem => JsonSerializer.Deserialize<SubscriptionRule>(elem.GetRawText())!).ToList();
    }

    public Task AddRuleAsync(string namespaceName, string topicName, string subscriptionName, string token, string ruleName, string filterType, object filterExpression, string? action)
        => CallVoidAsync("ServiceBusAPI.addRule", namespaceName, topicName, subscriptionName, token, ruleName, filterType, filterExpression, action);

    public Task RemoveRuleAsync(string namespaceName, string topicName, string subscriptionName, string token, string ruleName)
        => CallVoidAsync("ServiceBusAPI.removeRule", namespaceName, topicName, subscriptionName, token, ruleName);
}
