using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Service for managing background message search operations
/// </summary>
public sealed class BackgroundSearchService : IDisposable
{
    private readonly List<SearchOperation> _activeOperations = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    
    public event Action? OnOperationsChanged;
    public event Action<SearchOperation>? OnViewResultsRequested;
    
    public void RequestViewResults(SearchOperation operation)
    {
        OnViewResultsRequested?.Invoke(operation);
    }
    
    public IReadOnlyList<SearchOperation> ActiveOperations => _activeOperations.AsReadOnly();
    
    /// <summary>
    /// Time in seconds before completed/failed operations are auto-removed.
    /// Set to -1 to disable auto-removal for search operations (user must dismiss).
    /// </summary>
    public int AutoRemoveDelaySeconds { get; set; } = -1; // Don't auto-remove search results
    
    public BackgroundSearchService(IServiceScopeFactory scopeFactory, INotificationService notificationService)
    {
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
    }
    
    public async Task<string> StartSearchAsync(BackgroundSearchOptions options)
    {
        var operationId = Guid.NewGuid().ToString();
        var operation = new SearchOperation
        {
            Id = operationId,
            NamespaceName = options.NamespaceName,
            EntityType = options.EntityType,
            EntityPath = options.EntityPath,
            TopicName = options.TopicName,
            SubscriptionName = options.SubscriptionName,
            IsDeadLetter = options.IsDeadLetter,
            TotalMessageCount = options.TotalMessageCount,
            BodyFilter = options.BodyFilter,
            MessageIdFilter = options.MessageIdFilter,
            SubjectFilter = options.SubjectFilter,
            Status = SearchStatus.Running,
            StartTime = DateTime.Now
        };
        
        _activeOperations.Add(operation);
        NotifyChanged();
        
        // Start search in background using a new scope
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var jsInterop = scope.ServiceProvider.GetRequiredService<IServiceBusJsInteropService>();
            var notificationId = $"search-{operationId}";
            
            try
            {
                Console.WriteLine($"[BackgroundSearch] Starting search for {options.EntityPath} with filters: {options.GetFilterDescription()}");
                
                var token = await authService.GetServiceBusTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Service Bus token not available");
                }
                
                var callback = new SearchProgressCallback((scanned, matches, newSequenceNumbers) =>
                {
                    operation.MessagesScanned = scanned;
                    operation.MatchCount = matches;
                    foreach (var seq in newSequenceNumbers)
                    {
                        if (!operation.MatchingSequenceNumbers.Contains(seq))
                        {
                            operation.MatchingSequenceNumbers.Add(seq);
                        }
                    }
                    Console.WriteLine($"[BackgroundSearch] Progress: {scanned:N0}/{options.TotalMessageCount:N0} scanned, {matches} matches found");
                    NotifyChanged();
                });
                
                using var callbackRef = DotNetObjectReference.Create(callback);
                
                // Run the search
                IJSObjectReference? controller = null;
                
                if (options.EntityType == "queue")
                {
                    controller = await jsInterop.StartSearchQueueAsync(
                        options.NamespaceName,
                        options.EntityPath,
                        token,
                        callbackRef,
                        options.IsDeadLetter,
                        options.BodyFilter,
                        options.MessageIdFilter,
                        options.SubjectFilter,
                        (int)Math.Min(options.TotalMessageCount, 1_000_000),
                        options.MaxMatches);
                }
                else
                {
                    controller = await jsInterop.StartSearchSubscriptionAsync(
                        options.NamespaceName,
                        options.TopicName!,
                        options.SubscriptionName!,
                        token,
                        callbackRef,
                        options.IsDeadLetter,
                        options.BodyFilter,
                        options.MessageIdFilter,
                        options.SubjectFilter,
                        (int)Math.Min(options.TotalMessageCount, 1_000_000),
                        options.MaxMatches);
                }
                
                if (controller != null)
                {
                    operation.Controller = controller;
                    
                    // Wait for completion
                    var finalResult = await jsInterop.JSRuntime.InvokeAsync<SearchResult>("awaitControllerPromise", controller);
                    
                    operation.MessagesScanned = finalResult.ScannedCount;
                    operation.MatchCount = finalResult.MatchCount;
                    operation.MatchingSequenceNumbers = finalResult.MatchingSequenceNumbers.ToList();
                    operation.Status = SearchStatus.Completed;
                    operation.EndTime = DateTime.Now;
                    
                    _notificationService.NotifySuccess(
                        $"Search complete: Found {finalResult.MatchCount:N0} matches in {finalResult.ScannedCount:N0} messages", 
                        notificationId);
                }
                else
                {
                    throw new Exception("Failed to start search - controller is null");
                }
            }
            catch (TaskCanceledException)
            {
                operation.Status = SearchStatus.Cancelled;
                operation.EndTime = DateTime.Now;
                _notificationService.NotifyInfo($"Search cancelled for {options.EntityPath}", notificationId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackgroundSearch] ERROR: {ex.Message}");
                operation.Status = SearchStatus.Failed;
                operation.ErrorMessage = ex.Message;
                operation.EndTime = DateTime.Now;
                _notificationService.NotifyError($"Search failed for {options.EntityPath}: {ex.Message}", notificationId);
            }
            finally
            {
                NotifyChanged();
                
                // Auto-remove after delay if configured
                if (AutoRemoveDelaySeconds >= 0)
                {
                    await Task.Delay(AutoRemoveDelaySeconds * 1000);
                    _activeOperations.Remove(operation);
                    NotifyChanged();
                }
            }
        });
        
        return operationId;
    }
    
    public async Task CancelSearchAsync(string operationId)
    {
        var operation = _activeOperations.FirstOrDefault(o => o.Id == operationId);
        if (operation?.Controller != null && operation.Status == SearchStatus.Running)
        {
            try
            {
                operation.Status = SearchStatus.Stopping;
                NotifyChanged();
                await operation.Controller.InvokeVoidAsync("stop");
            }
            catch { }
        }
    }
    
    public void DismissOperation(string operationId)
    {
        var operation = _activeOperations.FirstOrDefault(o => o.Id == operationId);
        if (operation != null && operation.Status != SearchStatus.Running)
        {
            _activeOperations.Remove(operation);
            NotifyChanged();
        }
    }
    
    public SearchOperation? GetOperation(string operationId)
    {
        return _activeOperations.FirstOrDefault(o => o.Id == operationId);
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

public class SearchOperation
{
    public required string Id { get; init; }
    public required string NamespaceName { get; init; }
    public required string EntityType { get; init; }
    public required string EntityPath { get; init; }
    public string? TopicName { get; init; }
    public string? SubscriptionName { get; init; }
    public required bool IsDeadLetter { get; init; }
    public long TotalMessageCount { get; init; }
    
    // Filter criteria
    public string? BodyFilter { get; init; }
    public string? MessageIdFilter { get; init; }
    public string? SubjectFilter { get; init; }
    
    // Progress tracking
    public SearchStatus Status { get; set; }
    public int MessagesScanned { get; set; }
    public int MatchCount { get; set; }
    public List<long> MatchingSequenceNumbers { get; set; } = new();
    
    // Timing
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    
    // JS controller for cancellation
    public IJSObjectReference? Controller { get; set; }
    
    public string GetFilterSummary()
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(MessageIdFilter))
            filters.Add($"ID: {MessageIdFilter}");
        if (!string.IsNullOrWhiteSpace(SubjectFilter))
            filters.Add($"Subject: {SubjectFilter}");
        if (!string.IsNullOrWhiteSpace(BodyFilter))
            filters.Add($"Body: {BodyFilter}");
        return filters.Count > 0 ? string.Join(", ", filters) : "No filter";
    }
    
    public double ProgressPercentage => TotalMessageCount > 0 
        ? Math.Min(100, (double)MessagesScanned / TotalMessageCount * 100) 
        : 0;
}

public enum SearchStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    Stopping
}

/// <summary>
/// Result structure returned from JS search
/// </summary>
public class SearchResult
{
    public int ScannedCount { get; set; }
    public int MatchCount { get; set; }
    public long[] MatchingSequenceNumbers { get; set; } = Array.Empty<long>();
}
