using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Service for managing background resubmit operations
/// Singleton service that resolves scoped dependencies as needed
/// </summary>
public sealed class BackgroundResubmitService : IDisposable
{
    private readonly ConcurrentDictionary<string, ResubmitOperation> _activeOperations = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    
    /// <summary>An operation started, changed status or was removed.</summary>
    public event Action? OnOperationsChanged;
    public event Action<ResubmitOperation>? OnResubmitCompleted;

    public IReadOnlyList<ResubmitOperation> ActiveOperations => _activeOperations.Values.OrderBy(o => o.StartTime).ToList();
    
    public BackgroundResubmitService(IServiceScopeFactory scopeFactory, INotificationService notificationService)
    {
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
    }
    
    public async Task<string> StartResubmitAsync(string namespaceName, string entityType, string entityPath, bool deleteAfterResubmit)
    {
        var operationId = Guid.NewGuid().ToString();
        var operation = new ResubmitOperation
        {
            Id = operationId,
            NamespaceName = namespaceName,
            EntityType = entityType,
            EntityPath = entityPath,
            DeleteAfterResubmit = deleteAfterResubmit,
            Status = ResubmitStatus.Running,
            StartTime = DateTime.Now
        };
        
        _activeOperations[operationId] = operation;
        NotifyChanged();
        
        // Start resubmit in background using a new scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var operationsService = scope.ServiceProvider.GetRequiredService<IServiceBusOperationsService>();
            var jsInterop = scope.ServiceProvider.GetRequiredService<IServiceBusJsInteropService>();
            var notificationId = $"resubmit-{operationId}";
            
            try
            {
                // Parse entity path
                string? queueName = null;
                string? topicName = null;
                string? subscriptionName = null;
                
                if (entityType == "queue")
                {
                    queueName = entityPath;
                }
                else
                {
                    var parts = entityPath.Split('/');
                    if (parts.Length == 2)
                    {
                        topicName = parts[0];
                        subscriptionName = parts[1];
                    }
                    else if (parts.Length >= 3)
                    {
                        topicName = parts[0];
                        subscriptionName = parts[2];
                    }
                    else
                    {
                        throw new Exception($"Invalid subscription path: {entityPath}");
                    }
                }
                
                var dlqPath = queueName != null
                    ? $"{queueName}/$DeadLetterQueue"
                    : $"{topicName}/subscriptions/{subscriptionName}/$DeadLetterQueue";
                var token = await scope.ServiceProvider.GetRequiredService<ServiceBusTokenProvider>().GetTokenAsync(namespaceName, dlqPath);

                const int batchSize = 250;
                long fromSequence = 0;
                int totalResubmitted = 0;

                while (operation.Status == ResubmitStatus.Running)
                {
                    if (deleteAfterResubmit)
                    {
                        // Lock a batch from the head of the DLQ, send the copies, then complete the originals.
                        // An original only leaves the DLQ once its copy is sent; completed ones are gone, so the
                        // next receive picks up the following batch.
                        var locked = queueName != null
                            ? await jsInterop.ReceiveAndLockQueueMessagesAsync(namespaceName, queueName, token, 5, true, batchSize)
                            : await jsInterop.ReceiveAndLockSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, token, 5, true, batchSize);
                        if (locked.Count == 0) break;

                        var lockTokens = locked.Select(m => m.LockToken!).ToArray();
                        try
                        {
                            await operationsService.SendResubmitCopiesAsync(namespaceName, queueName, topicName, locked, fromDeadLetter: true);
                        }
                        catch
                        {
                            // Nothing was resubmitted: put the batch back, but report the send failure rather than this
                            try { await operationsService.AbandonMessagesAsync(lockTokens); } catch { }
                            throw;
                        }

                        var completion = await operationsService.CompleteMessagesAsync(lockTokens);
                        totalResubmitted += locked.Count;
                        operation.MessagesResubmitted = totalResubmitted;
                        if (completion.FailureCount > 0)
                        {
                            // Those originals go back to the DLQ head, so carrying on would resend them again
                            throw new Exception($"{completion.FailureCount} message(s) were resubmitted but could not be removed from the DLQ, so they now exist in both. Stopped after {totalResubmitted:N0} resubmitted.");
                        }
                    }
                    else
                    {
                        // Copies only: page through the DLQ and leave the originals where they are
                        var peeked = queueName != null
                            ? await jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, batchSize, fromSequence, true)
                            : await jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, token, batchSize, fromSequence, true);
                        if (peeked.Count == 0) break;

                        await operationsService.SendResubmitCopiesAsync(namespaceName, queueName, topicName, peeked, fromDeadLetter: true);
                        totalResubmitted += peeked.Count;
                        operation.MessagesResubmitted = totalResubmitted;
                        fromSequence = peeked.Max(m => m.SequenceNumber ?? 0) + 1;
                    }

                    // Small delay to avoid overwhelming the service
                    await Task.Delay(100);
                }
                
                operation.Status = operation.Status == ResubmitStatus.Running
                    ? ResubmitStatus.Completed
                    : ResubmitStatus.Cancelled;
                operation.EndTime = DateTime.Now;
                OnResubmitCompleted?.Invoke(operation);

                string typeLabel = entityType == "queue" ? "queue" : "subscription";
                _notificationService.NotifySuccess($"Resubmit complete: {totalResubmitted:N0} messages resubmitted from {typeLabel} '{entityPath}' DLQ", notificationId);
            }
            catch (Exception ex)
            {
                operation.Status = ResubmitStatus.Failed;
                operation.ErrorMessage = ex.Message;
                operation.EndTime = DateTime.Now;
                
                _notificationService.NotifyError($"Resubmit failed for {entityPath}: {ex.Message}", notificationId);
            }
            finally
            {
                NotifyChanged();
                // Remove completed operations after 10 seconds
                await Task.Delay(10000);
                _activeOperations.TryRemove(operation.Id, out _);
                NotifyChanged();
            }
        });
        
        return operationId;
    }
    
    public void CancelResubmit(string operationId)
    {
        _activeOperations.TryGetValue(operationId, out var operation);
        if (operation != null)
        {
            operation.Status = ResubmitStatus.Cancelled;
            NotifyChanged();
        }
    }
    
    private void NotifyChanged() => OnOperationsChanged?.Invoke();
    
    public void Dispose()
    {
        _activeOperations.Clear();
    }
}

public class ResubmitOperation
{
    public required string Id { get; init; }
    public required string NamespaceName { get; init; }
    public required string EntityType { get; init; }
    public required string EntityPath { get; init; }
    public required bool DeleteAfterResubmit { get; init; }
    public ResubmitStatus Status { get; set; }
    public int MessagesResubmitted { get; set; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ResubmitStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}
