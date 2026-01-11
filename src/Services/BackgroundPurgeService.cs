using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Service for managing background purge operations
/// Singleton service that resolves scoped dependencies as needed
/// </summary>
public sealed class BackgroundPurgeService : IDisposable
{
    private readonly List<PurgeOperation> _activeOperations = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    
    public event Action? OnOperationsChanged;
    
    public IReadOnlyList<PurgeOperation> ActiveOperations => _activeOperations.AsReadOnly();
    
    public BackgroundPurgeService(IServiceScopeFactory scopeFactory, INotificationService notificationService)
    {
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
    }
    
    public async Task<string> StartPurgeAsync(string namespaceName, string entityType, string entityPath, bool isDeadLetter)
    {
        var operationId = Guid.NewGuid().ToString();
        var operation = new PurgeOperation
        {
            Id = operationId,
            NamespaceName = namespaceName,
            EntityType = entityType,
            EntityPath = entityPath,
            IsDeadLetter = isDeadLetter,
            Status = PurgeStatus.Running,
            StartTime = DateTime.Now
        };
        
        _activeOperations.Add(operation);
        NotifyChanged();
        
        // Start purge in background using a new scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var jsInterop = scope.ServiceProvider.GetRequiredService<IServiceBusJsInteropService>();
            var notificationId = $"purge-{operationId}";
            
            try
            {
                Console.WriteLine($"[BackgroundPurge] Starting purge for {entityType} {entityPath}");
                
                var token = await authService.GetServiceBusTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Service Bus token not available");
                }
                
                Console.WriteLine($"[BackgroundPurge] Got token, creating callback");
                var callback = new PurgeProgressCallback(count =>
                {
                    operation.MessagesDeleted = count;
                    Console.WriteLine($"[BackgroundPurge] Progress: {count} messages deleted");
                    NotifyChanged();
                });
                
                using var callbackRef = DotNetObjectReference.Create(callback);
                
                IJSObjectReference? controller = null;
                
                if (entityType == "queue")
                {
                    Console.WriteLine($"[BackgroundPurge] Calling StartPurgeQueueAsync for {entityPath}");
                    controller = await jsInterop.StartPurgeQueueAsync(
                        namespaceName,
                        entityPath,
                        token,
                        callbackRef,
                        isDeadLetter);
                }
                else
                {
                    Console.WriteLine($"[BackgroundPurge] Calling StartPurgeSubscriptionAsync for {entityPath}");
                    var parts = entityPath.Split('/');
                    
                    // EntityPath can be "topic/subscription" or "topic/subscriptions/subscription"
                    string topicName, subscriptionName;
                    if (parts.Length == 2)
                    {
                        // Format: "topic/subscription"
                        topicName = parts[0];
                        subscriptionName = parts[1];
                    }
                    else if (parts.Length >= 3)
                    {
                        // Format: "topic/subscriptions/subscription"
                        topicName = parts[0];
                        subscriptionName = parts[2];
                    }
                    else
                    {
                        throw new Exception($"Invalid subscription path: {entityPath}. Expected 'topic/subscription' or 'topic/subscriptions/subscription'");
                    }
                    
                    Console.WriteLine($"[BackgroundPurge] Topic: {topicName}, Subscription: {subscriptionName}");
                    controller = await jsInterop.StartPurgeSubscriptionAsync(
                        namespaceName,
                        topicName,
                        subscriptionName,
                        token,
                        callbackRef,
                        isDeadLetter);
                }
                
                if (controller != null)
                {
                    Console.WriteLine($"[BackgroundPurge] Got controller, waiting for completion");
                    operation.Controller = controller;
                    var finalCount = await jsInterop.JSRuntime.InvokeAsync<int>("awaitControllerPromise", controller);
                    Console.WriteLine($"[BackgroundPurge] Purge complete: {finalCount} messages deleted");
                    operation.MessagesDeleted = finalCount;
                    operation.Status = PurgeStatus.Completed;
                    operation.EndTime = DateTime.Now;
                    
                    // Show success notification (updates the progress notification)
                    string typeLabel = entityType == "queue" ? "queue" : "subscription";
                    _notificationService.NotifySuccess($"Purge complete: {finalCount:N0} messages deleted from {typeLabel} '{entityPath}'", notificationId);
                }
                else
                {
                    Console.WriteLine($"[BackgroundPurge] ERROR: Controller is null!");
                    throw new Exception("Failed to start purge - controller is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackgroundPurge] ERROR: {ex.Message}");
                Console.WriteLine($"[BackgroundPurge] Stack trace: {ex.StackTrace}");
                operation.Status = PurgeStatus.Failed;
                operation.ErrorMessage = ex.Message;
                operation.EndTime = DateTime.Now;
                
                // Show error notification (updates the progress notification)
                _notificationService.NotifyError($"Purge failed for {entityPath}: {ex.Message}", notificationId);
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
    
    public async Task CancelPurgeAsync(string operationId)
    {
        var operation = _activeOperations.FirstOrDefault(o => o.Id == operationId);
        if (operation?.Controller != null)
        {
            try
            {
                operation.Status = PurgeStatus.Stopping;
                NotifyChanged();
                await operation.Controller.InvokeVoidAsync("stop");
                // Do not remove immediately; let the background task complete and clean up
            }
            catch { }
            // NotifyChanged will be called by catch or finally in the background task eventually
        }
    }
    
    private void NotifyChanged() => OnOperationsChanged?.Invoke();
    
    public void Dispose()
    {
        foreach (var operation in _activeOperations)
        {
            operation.Controller?.DisposeAsync();
        }
        _activeOperations.Clear();
    }
}

public class PurgeOperation
{
    public required string Id { get; init; }
    public required string NamespaceName { get; init; }
    public required string EntityType { get; init; }
    public required string EntityPath { get; init; }
    public required bool IsDeadLetter { get; init; }
    public PurgeStatus Status { get; set; }
    public int MessagesDeleted { get; set; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    public IJSObjectReference? Controller { get; set; }
}

public enum PurgeStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    Stopping
}
