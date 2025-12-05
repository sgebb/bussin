using Microsoft.JSInterop;
using System.Text.Json;
using ServiceBusExplorer.Blazor.Models;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class ServiceBusJsInteropService(IJSRuntime jsRuntime) : IServiceBusJsInteropService
{
    public IJSRuntime JSRuntime => jsRuntime;
    
    /// <summary>
    /// Safely deserialize a ServiceBusMessage from JSON, handling errors gracefully.
    /// If deserialization fails due to malformed data, logs the error and returns null.
    /// </summary>
    private static ServiceBusMessage? SafeDeserializeMessage(JsonElement elem)
    {
        try
        {
            var json = elem.GetRawText();
            var message = JsonSerializer.Deserialize<ServiceBusMessage>(json);
            if (message == null) return null;

            // Preserve original body and content type for exact format preservation
            message.OriginalBody = elem.TryGetProperty("originalBody", out var originalBody) ? originalBody : null;
            message.OriginalContentType = elem.TryGetProperty("originalContentType", out var originalContentType) ? originalContentType.GetString() : null;

            return message;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"⚠️ Failed to deserialize message: {ex.Message}");
            Console.WriteLine($"   Problem: {ex.Path} - {ex.InnerException?.Message}");
            
            // Try to extract at least the body and messageId for display
            try
            {
                var bodyProp = elem.TryGetProperty("body", out var body) ? body.GetString() : "[Unable to parse body]";
                var msgIdProp = elem.TryGetProperty("messageId", out var msgId) ? msgId.GetString() : null;
                
                return new ServiceBusMessage
                {
                    MessageId = msgIdProp,
                    Body = bodyProp ?? "[Error parsing message]",
                    ContentType = "text/plain",
                    OriginalBody = elem.TryGetProperty("originalBody", out var originalBody) ? originalBody : null,
                    OriginalContentType = elem.TryGetProperty("originalContentType", out var originalContentType) ? originalContentType.GetString() : null,
                    LockToken = elem.TryGetProperty("lockToken", out var lockToken) ? lockToken.GetString() : null
                };
            }
            catch
            {
                // If we can't even extract basic info, return null
                Console.WriteLine($"⚠️ Could not extract any message data");
                return null;
            }
        }
    }
    
    // Queue Operations
    
    public async Task<List<ServiceBusMessage>> PeekQueueMessagesAsync(string namespaceName, string queueName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
    {
        try
        {
            Console.WriteLine($"Calling JS: peekQueueMessages({namespaceName}, {queueName}, token[{token.Length} chars], {count}, {fromSequence}, {fromDeadLetter})");
            
            var result = await jsRuntime.InvokeAsync<JsonElement[]>(
                "ServiceBusAPI.peekQueueMessages",
                namespaceName, queueName, token, count, fromSequence, fromDeadLetter);
            
            Console.WriteLine($"✓ JS returned {result.Length} messages");
            
            return result.Select(SafeDeserializeMessage)
                .OfType<ServiceBusMessage>()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ JS Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
            throw;
        }
    }

    public async Task SendQueueMessageAsync(string namespaceName, string queueName, string token, object messageBody, MessageProperties? properties = null)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(
                "ServiceBusAPI.sendQueueMessage",
                namespaceName, queueName, token, messageBody, properties);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending queue message: {ex.Message}");
            throw;
        }
    }

    public async Task<int> PurgeQueueAsync(string namespaceName, string queueName, string token, bool fromDeadLetter = false)
    {
        try
        {
            // Call the JS API which returns a PurgeController with a promise property
            var controllerRef = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "ServiceBusAPI.purgeQueue",
                namespaceName, queueName, token, null, fromDeadLetter);
            
            // Access the promise property and await it using a helper
            var count = await jsRuntime.InvokeAsync<int>(
                "awaitControllerPromise",
                controllerRef);
            
            Console.WriteLine($"Purge completed: {count} messages deleted");
            return count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error purging queue: {ex.Message}");
            throw;
        }
    }

    // Lock-based operations (new API)
    
    public async Task<List<ServiceBusMessage>> ReceiveAndLockQueueMessagesAsync(string namespaceName, string queueName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement[]>(
                "ServiceBusAPI.receiveAndLockQueueMessage",
                namespaceName, queueName, token, timeoutSeconds, fromDeadLetter, count);
            
            return result.Select(SafeDeserializeMessage)
                .OfType<ServiceBusMessage>()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error locking queue messages: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ServiceBusMessage>> ReceiveAndLockSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int timeoutSeconds = 5, bool fromDeadLetter = false, int count = 1)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement[]>(
                "ServiceBusAPI.receiveAndLockSubscriptionMessage",
                namespaceName, topicName, subscriptionName, token, timeoutSeconds, fromDeadLetter, count);
            
            return result.Select(SafeDeserializeMessage)
                .OfType<ServiceBusMessage>()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error locking subscription messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> CompleteMessagesAsync(string[] lockTokens)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement>(
                "ServiceBusAPI.complete",
                new object[] { lockTokens });
            
            return JsonSerializer.Deserialize<BatchOperationResult>(result.GetRawText())!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error completing messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> AbandonMessagesAsync(string[] lockTokens)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement>(
                "ServiceBusAPI.abandon",
                new object[] { lockTokens });
            
            return JsonSerializer.Deserialize<BatchOperationResult>(result.GetRawText())!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error abandoning messages: {ex.Message}");
            throw;
        }
    }

    public async Task<BatchOperationResult> DeadLetterMessagesAsync(string[] lockTokens, DeadLetterOptions? options = null)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement>(
                "ServiceBusAPI.deadLetter",
                lockTokens, options ?? new DeadLetterOptions());
            
            return JsonSerializer.Deserialize<BatchOperationResult>(result.GetRawText())!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error dead lettering messages: {ex.Message}");
            throw;
        }
    }

    // Topic/Subscription Operations
    
    public async Task<List<ServiceBusMessage>> PeekSubscriptionMessagesAsync(string namespaceName, string topicName, string subscriptionName, string token, int count = 10, int fromSequence = 0, bool fromDeadLetter = false)
    {
        try
        {
            var result = await jsRuntime.InvokeAsync<JsonElement[]>(
                "ServiceBusAPI.peekSubscriptionMessages",
                namespaceName, topicName, subscriptionName, token, count, fromSequence, fromDeadLetter);
            
            return result.Select(SafeDeserializeMessage)
                .OfType<ServiceBusMessage>()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error peeking subscription messages: {ex.Message}");
            throw;
        }
    }

    public async Task SendTopicMessageAsync(string namespaceName, string topicName, string token, object messageBody, MessageProperties? properties = null)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(
                "ServiceBusAPI.sendTopicMessage",
                namespaceName, topicName, token, messageBody, properties);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending topic message: {ex.Message}");
            throw;
        }
    }

    public async Task<int> PurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, bool fromDeadLetter = false)
    {
        try
        {
            // Call the JS API which returns a PurgeController with a promise property
            var controllerRef = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "ServiceBusAPI.purgeSubscription",
                namespaceName, topicName, subscriptionName, token, null, fromDeadLetter);
            
            // Access the promise property and await it using a helper
            var count = await jsRuntime.InvokeAsync<int>(
                "awaitControllerPromise",
                controllerRef);
            
            Console.WriteLine($"Purge completed: {count} messages deleted");
            return count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error purging subscription: {ex.Message}");
            throw;
        }
    }

    // Monitor operations (continuous non-destructive)
    
    public async Task<IJSObjectReference> StartMonitoringQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        try
        {
            // Call the JavaScript helper function directly
            var controller = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "startMonitoringQueue",
                namespaceName, 
                queueName, 
                token, 
                callbackRef);
            
            return controller;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting queue monitor: {ex.Message}");
            throw;
        }
    }

    public async Task<IJSObjectReference> StartMonitoringSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<MessageMonitorCallback> callbackRef)
    {
        try
        {
            // Call the JavaScript helper function directly
            var controller = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "startMonitoringSubscription",
                namespaceName, 
                topicName, 
                subscriptionName, 
                token, 
                callbackRef);
            
            return controller;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting subscription monitor: {ex.Message}");
            throw;
        }
    }

    public async Task StopMonitoringAsync(IJSObjectReference monitorController)
    {
        try
        {
            await monitorController.InvokeVoidAsync("stop");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping monitor: {ex.Message}");
            throw;
        }
    }

    // Purge with progress
    
    public async Task<IJSObjectReference> StartPurgeQueueAsync(string namespaceName, string queueName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
        try
        {
            // Call JS function that wraps the purge with progress callback
            var controller = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "startPurgeWithProgress",
                namespaceName, queueName, token, callbackRef, fromDeadLetter, "queue");
            
            return controller;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting purge: {ex.Message}");
            throw;
        }
    }

    public async Task<IJSObjectReference> StartPurgeSubscriptionAsync(string namespaceName, string topicName, string subscriptionName, string token, DotNetObjectReference<PurgeProgressCallback> callbackRef, bool fromDeadLetter = false)
    {
        try
        {
            // Call JS function that wraps the purge with progress callback
            var controller = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "startPurgeWithProgress",
                namespaceName, topicName, subscriptionName, token, callbackRef, fromDeadLetter, "subscription");
            
            return controller;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting purge: {ex.Message}");
            throw;
        }
    }

}
