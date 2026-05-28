using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Service for managing background resubmit operations
/// Singleton service that resolves scoped dependencies as needed
/// </summary>
public sealed class BackgroundResubmitService : IDisposable
{
    private readonly List<ResubmitOperation> _activeOperations = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    
    public event Action? OnOperationsChanged;
    
    public IReadOnlyList<ResubmitOperation> ActiveOperations => _activeOperations.AsReadOnly();
    
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
        
        _activeOperations.Add(operation);
        NotifyChanged();
        
        // Start resubmit in background using a new scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var operationsService = scope.ServiceProvider.GetRequiredService<IServiceBusOperationsService>();
            var jsInterop = scope.ServiceProvider.GetRequiredService<IServiceBusJsInteropService>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var notificationId = $"resubmit-{operationId}";
            
            try
            {
                Console.WriteLine($"[BackgroundResubmit] Starting resubmit for {entityType} {entityPath}");
                
                var token = await authService.GetServiceBusTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Service Bus token not available");
                }
                
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
                
                // Iteratively peek all messages from DLQ and resubmit them
                const int batchSize = 250;
                int fromSequence = 0;
                int totalResubmitted = 0;
                bool hasMoreMessages = true;
                
                while (hasMoreMessages && operation.Status == ResubmitStatus.Running)
                {
                    // Peek a batch of messages from DLQ
                    List<ServiceBusMessage> messages;
                    
                    if (queueName != null)
                    {
                        messages = await jsInterop.PeekQueueMessagesAsync(namespaceName, queueName, token, batchSize, fromSequence, true);
                    }
                    else
                    {
                        messages = await jsInterop.PeekSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, token, batchSize, fromSequence, true);
                    }
                    
                    if (messages.Count == 0)
                    {
                        hasMoreMessages = false;
                        break;
                    }
                    
                    Console.WriteLine($"[BackgroundResubmit] Peeked {messages.Count} messages from DLQ");
                    
                    // Get sequence numbers for this batch
                    var sequenceNumbers = messages
                        .Where(m => m.SequenceNumber.HasValue)
                        .Select(m => m.SequenceNumber!.Value)
                        .ToArray();
                    
                    if (sequenceNumbers.Length > 0)
                    {
                        // Resubmit this batch
                        if (queueName != null)
                        {
                            await operationsService.ResendQueueMessagesAsync(namespaceName, queueName, sequenceNumbers, true, deleteAfterResubmit);
                        }
                        else
                        {
                            await operationsService.ResendSubscriptionMessagesAsync(namespaceName, topicName!, subscriptionName!, sequenceNumbers, true, deleteAfterResubmit);
                        }
                        
                        totalResubmitted += sequenceNumbers.Length;
                        operation.MessagesResubmitted = totalResubmitted;
                        NotifyChanged();
                        
                        Console.WriteLine($"[BackgroundResubmit] Resubmitted {sequenceNumbers.Length} messages (total: {totalResubmitted})");
                    }
                    
                    // Update sequence for next batch
                    var maxSeq = messages.Max(m => m.SequenceNumber ?? 0);
                    fromSequence = (int)(maxSeq + 1);
                    
                    // If we got fewer messages than batch size, we're done
                    if (messages.Count < batchSize)
                    {
                        hasMoreMessages = false;
                    }
                    
                    // Small delay to avoid overwhelming the service
                    await Task.Delay(100);
                }
                
                operation.Status = ResubmitStatus.Completed;
                operation.EndTime = DateTime.Now;
                
                string typeLabel = entityType == "queue" ? "queue" : "subscription";
                _notificationService.NotifySuccess($"Resubmit complete: {totalResubmitted:N0} messages resubmitted from {typeLabel} '{entityPath}' DLQ", notificationId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackgroundResubmit] ERROR: {ex.Message}");
                Console.WriteLine($"[BackgroundResubmit] Stack trace: {ex.StackTrace}");
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
                _activeOperations.Remove(operation);
                NotifyChanged();
            }
        });
        
        return operationId;
    }
    
    public void CancelResubmit(string operationId)
    {
        var operation = _activeOperations.FirstOrDefault(o => o.Id == operationId);
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
