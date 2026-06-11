using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Azure.Core;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// State container and service managing namespace-level discoveries,
/// entity selections, and registered queues/topics/subscriptions.
/// </summary>
public sealed class EntitySelectionState : IDisposable
{
    private readonly IAuthenticationService _authService;
    private readonly IAzureResourceService _resourceService;
    private readonly NavigationStateService _navState;
    private readonly INotificationService _notificationService;
    private readonly IConfirmModalService _confirmModal;
    private readonly NavigationManager _navigation;
    private readonly ServiceBusEntityCache _cache;

    public EntitySelectionState(
        IAuthenticationService authService,
        IAzureResourceService resourceService,
        NavigationStateService navState,
        INotificationService notificationService,
        IConfirmModalService confirmModal,
        NavigationManager navigation,
        ServiceBusEntityCache cache)
    {
        _authService = authService;
        _resourceService = resourceService;
        _navState = navState;
        _notificationService = notificationService;
        _confirmModal = confirmModal;
        _navigation = navigation;
        _cache = cache;
    }

    public event Action? OnChange;
    public event Action? OnEntitySelectionChanged;

    private void NotifyStateChanged() => OnChange?.Invoke();

    // State
    public ExplorerState State { get; } = new();
    public Dictionary<string, ServiceBusQueueInfo> QueueDict { get; } = new();
    public Dictionary<string, ServiceBusTopicInfo> TopicDict { get; } = new();
    public Dictionary<string, ServiceBusSubscriptionInfo> SubscriptionDict { get; } = new();

    public string DisplayName { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? RawErrorMessage { get; set; }
    public bool IsConnectionStringMode { get; set; }
    
    // UI Visibility State
    public bool IsLoadingEntities { get; set; }
    public bool IsLoadingSubscriptions { get; set; }

    private CancellationTokenSource? _entitiesLoadCts;
    private CancellationTokenSource? _subscriptionsLoadCts;
    private string? _loadedSubscriptionsForTopic;
    
    public string? LoadedSubscriptionsForTopic => _loadedSubscriptionsForTopic;

    // Computed
    public List<ServiceBusQueueInfo> Queues => QueueDict.Values.OrderBy(q => q.Name).ToList();
    public List<ServiceBusTopicInfo> Topics => TopicDict.Values.OrderBy(t => t.Name).ToList();
    public List<ServiceBusSubscriptionInfo> Subscriptions => SubscriptionDict.Values.OrderBy(s => s.Name).ToList();
    
    public string NamespaceNameOnly => State.NamespaceNameOnly;
    public bool HasEntitySelected => State.HasEntitySelected;
    public bool HasQueueOrSubscriptionSelected => State.HasQueueOrSubscriptionSelected;
    public bool IsFavorite => _navState.IsFavorite(State.FullyQualifiedNamespace);

    public long CurrentEntityMessageCount
    {
        get
        {
            if (State.SelectedQueueName != null && QueueDict.TryGetValue(State.SelectedQueueName, out var queue))
            {
                return State.IsViewingDLQ ? queue.DeadLetterMessageCount : queue.ActiveMessageCount;
            }
            else if (State.SelectedSubscriptionName != null && SubscriptionDict.TryGetValue(State.SelectedSubscriptionName, out var sub))
            {
                return State.IsViewingDLQ ? sub.DeadLetterMessageCount : sub.ActiveMessageCount;
            }
            return 0;
        }
    }

    public bool SelectedEntityRequiresSession
    {
        get
        {
            if (State.SelectedQueueName != null && QueueDict.TryGetValue(State.SelectedQueueName, out var queue))
            {
                return queue.RequiresSession;
            }
            else if (State.SelectedSubscriptionName != null && SubscriptionDict.TryGetValue(State.SelectedSubscriptionName, out var sub))
            {
                return sub.RequiresSession;
            }
            return false;
        }
    }

    public async Task InitializeAsync(
        string? namespaceParam, 
        string? resourceGroupParam, 
        string? subscriptionIdParam, 
        string? nameParam,
        string? queueParam,
        string? topicParam,
        string? subscriptionParam,
        bool dlqParam)
    {
        bool isNewNamespace = namespaceParam != State.CurrentNamespace?.FullyQualifiedNamespace;
        
        if (isNewNamespace && !string.IsNullOrEmpty(namespaceParam))
        {
            var connection = _navState.GetNamespaceConnection(namespaceParam);
            IsConnectionStringMode = connection != null && !string.IsNullOrEmpty(connection.ConnectionString);
            
            State.SetNamespace(new ServiceBusNamespaceInfo
            {
                Name = namespaceParam.Split('.').FirstOrDefault() ?? "",
                FullyQualifiedNamespace = namespaceParam,
                ResourceGroup = (string.IsNullOrEmpty(resourceGroupParam) ? connection?.ResourceGroup : resourceGroupParam) ?? "",
                SubscriptionId = (string.IsNullOrEmpty(subscriptionIdParam) ? connection?.SubscriptionId : subscriptionIdParam) ?? ""
            });

            QueueDict.Clear();
            TopicDict.Clear();
            SubscriptionDict.Clear();
            _loadedSubscriptionsForTopic = null;

            DisplayName = connection?.DisplayName ?? nameParam ?? "";

            if (IsConnectionStringMode)
            {
                await LoadConnectionStringEntitiesAsync(connection!);
            }
            else
            {
                if (!string.IsNullOrEmpty(State.CurrentNamespace?.SubscriptionId) && 
                    !string.IsNullOrEmpty(State.CurrentNamespace?.ResourceGroup))
                {
                    await LoadEntitiesAsync();
                }
            }
        }

        bool selectionChanged = false;

        if (queueParam != State.SelectedQueueName)
        {
            State.SelectQueue(queueParam!);
            selectionChanged = true;
        }

        if (topicParam != State.SelectedTopicName || subscriptionParam != State.SelectedSubscriptionName)
        {
            if (topicParam != State.SelectedTopicName)
            {
                _loadedSubscriptionsForTopic = null;
                SubscriptionDict.Clear();
                State.SelectTopic(topicParam!);
                selectionChanged = true;
                if (topicParam != null)
                {
                    await LoadSubscriptionsAsync(topicParam);
                }
            }
            if (subscriptionParam != State.SelectedSubscriptionName)
            {
                State.SelectSubscription(subscriptionParam!);
                selectionChanged = true;
            }
        }

        if (queueParam == null && topicParam == null)
        {
            if (State.SelectedQueueName != null || State.SelectedTopicName != null)
            {
                State.ClearSelection();
                selectionChanged = true;
            }
        }

        if (dlqParam != State.IsViewingDLQ)
        {
            State.IsViewingDLQ = dlqParam;
            selectionChanged = true;
        }

        if (selectionChanged || isNewNamespace)
        {
            OnEntitySelectionChanged?.Invoke();
            NotifyStateChanged();
        }
    }

    public async Task LoadEntitiesAsync()
    {
        if (State.CurrentNamespace == null || 
            string.IsNullOrEmpty(State.CurrentNamespace.SubscriptionId) || 
            string.IsNullOrEmpty(State.CurrentNamespace.ResourceGroup)) 
        {
            return;
        }

        _entitiesLoadCts?.Cancel();
        var myCts = new CancellationTokenSource();
        _entitiesLoadCts = myCts;
        var ct = myCts.Token;

        IsLoadingEntities = true;
        ErrorMessage = null;

        // Immediately seed from cache for instant UI response
        var cacheKey = $"{State.CurrentNamespace.SubscriptionId}/{State.CurrentNamespace.ResourceGroup}/{State.CurrentNamespace.Name}";
        var cachedQueues = _cache.GetQueues(cacheKey);
        if (cachedQueues != null && cachedQueues.Count > 0)
        {
            QueueDict.Clear();
            foreach (var q in cachedQueues)
            {
                QueueDict[q.Name] = q;
            }
        }
        var cachedTopics = _cache.GetTopics(cacheKey);
        if (cachedTopics != null && cachedTopics.Count > 0)
        {
            TopicDict.Clear();
            foreach (var t in cachedTopics)
            {
                TopicDict[t.Name] = t;
            }
        }
        NotifyStateChanged();

        try
        {
            var credential = await _authService.GetTokenCredentialAsync();
            if (credential == null)
            {
                if (_entitiesLoadCts == myCts)
                {
                    ErrorMessage = "Failed to get authentication token";
                }
                return;
            }

            if (ct.IsCancellationRequested) return;

            var seenQueues = new HashSet<string>();
            var seenTopics = new HashSet<string>();

            await Task.WhenAll(
                LoadQueuesAsync(credential, State.CurrentNamespace, seenQueues, ct),
                LoadTopicsAsync(credential, State.CurrentNamespace, seenTopics, ct)
            );

            if (ct.IsCancellationRequested) return;

            if (_entitiesLoadCts == myCts)
            {
                var queuesToRemove = QueueDict.Keys.Where(k => !seenQueues.Contains(k)).ToList();
                foreach (var key in queuesToRemove)
                {
                    QueueDict.Remove(key);
                }

                var topicsToRemove = TopicDict.Keys.Where(k => !seenTopics.Contains(k)).ToList();
                foreach (var key in topicsToRemove)
                {
                    TopicDict.Remove(key);
                }
            }
        }
        catch (Exception ex)
        {
            if (_entitiesLoadCts == myCts)
            {
                ErrorMessage = $"Failed to load entities: {ex.Message}";
            }
        }
        finally
        {
            if (_entitiesLoadCts == myCts)
            {
                IsLoadingEntities = false;
            }
            NotifyStateChanged();
        }
    }

    private async Task LoadQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, HashSet<string> seenQueues, CancellationToken ct)
    {
        try
        {
            await foreach (var queue in _resourceService.ListQueuesAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                if (State.CurrentNamespace?.FullyQualifiedNamespace == namespaceInfo.FullyQualifiedNamespace)
                {
                    QueueDict[queue.Name] = queue;
                    seenQueues.Add(queue.Name);
                    NotifyStateChanged();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, HashSet<string> seenTopics, CancellationToken ct)
    {
        try
        {
            await foreach (var topic in _resourceService.ListTopicsAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                if (State.CurrentNamespace?.FullyQualifiedNamespace == namespaceInfo.FullyQualifiedNamespace)
                {
                    TopicDict[topic.Name] = topic;
                    seenTopics.Add(topic.Name);
                    NotifyStateChanged();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshEntitiesAsync()
    {
        await LoadEntitiesAsync();

        if (State.SelectedTopicName != null)
        {
            await LoadSubscriptionsAsync(State.SelectedTopicName);
        }

        _notificationService.NotifySuccess("Refreshed queues and topics");
    }

    public void SelectQueue(string queueName)
    {
        if (State.SelectedQueueName != queueName)
        {
            State.SelectQueue(queueName);
            OnEntitySelectionChanged?.Invoke();
            NotifyStateChanged();
        }
    }

    public async Task SelectTopic(string topicName)
    {
        if (State.SelectedTopicName != topicName)
        {
            _loadedSubscriptionsForTopic = null;
            SubscriptionDict.Clear();
            State.SelectTopic(topicName);
            OnEntitySelectionChanged?.Invoke();
        }
        if (topicName != null)
        {
            await LoadSubscriptionsAsync(topicName);
        }
        NotifyStateChanged();
    }

    public void SelectSubscription(string subName)
    {
        if (State.SelectedSubscriptionName != subName)
        {
            State.SelectSubscription(subName);
            OnEntitySelectionChanged?.Invoke();
            NotifyStateChanged();
        }
    }

    public void ToggleDLQ()
    {
        State.IsViewingDLQ = !State.IsViewingDLQ;
        OnEntitySelectionChanged?.Invoke();
        NotifyStateChanged();
    }

    public async Task ToggleFavoriteAsync()
    {
        if (State.CurrentNamespace == null) return;
        var fqn = State.CurrentNamespace.FullyQualifiedNamespace;
        
        if (IsFavorite)
        {
            await _navState.RemoveFromFavoritesAsync(fqn);
            _notificationService.NotifySuccess("Removed from favorites");
        }
        else
        {
            await _navState.AddToFavoritesAsync(fqn, State.CurrentNamespace.ResourceGroup, State.CurrentNamespace.SubscriptionId, DisplayName);
            _notificationService.NotifySuccess("Added to favorites");
        }
        NotifyStateChanged();
    }

    private async Task LoadSubscriptionsAsync(string topicName)
    {
        if (State.CurrentNamespace == null) return;

        if (IsConnectionStringMode)
        {
            SubscriptionDict.Clear();
            var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
            var topic = connection?.ConfiguredTopics.FirstOrDefault(t => t.TopicName == topicName);
            if (topic != null)
            {
                foreach (var subName in topic.Subscriptions)
                {
                    SubscriptionDict[subName] = new ServiceBusSubscriptionInfo
                    {
                        Name = subName,
                        Status = "Active",
                        ActiveMessageCount = 0,
                        DeadLetterMessageCount = 0,
                        RequiresSession = false
                    };
                }
            }
            _loadedSubscriptionsForTopic = topicName;
            NotifyStateChanged();
            return;
        }

        _subscriptionsLoadCts?.Cancel();
        var myCts = new CancellationTokenSource();
        _subscriptionsLoadCts = myCts;
        var ct = myCts.Token;

        IsLoadingSubscriptions = true;

        if (_loadedSubscriptionsForTopic != topicName)
        {
            SubscriptionDict.Clear();

            // Immediately load from cache for instant UI response
            var cacheKey = $"{State.CurrentNamespace.SubscriptionId}/{State.CurrentNamespace.ResourceGroup}/{State.CurrentNamespace.Name}/{topicName}";
            var cached = _cache.GetSubscriptions(cacheKey);
            if (cached != null && cached.Count > 0)
            {
                foreach (var sub in cached)
                {
                    SubscriptionDict[sub.Name] = sub;
                }
            }
        }
        NotifyStateChanged();

        try
        {
            var credential = await _authService.GetTokenCredentialAsync();
            if (credential == null)
            {
                return;
            }

            if (ct.IsCancellationRequested) return;

            var seenSubs = new HashSet<string>();
            var firstItem = true;
            await foreach (var sub in _resourceService.ListSubscriptionsAsync(credential, State.CurrentNamespace, topicName, ct))
            {
                if (ct.IsCancellationRequested) break;

                if (_subscriptionsLoadCts == myCts && State.SelectedTopicName == topicName)
                {
                    SubscriptionDict[sub.Name] = sub;
                    seenSubs.Add(sub.Name);
                }

                if (firstItem)
                {
                    if (_subscriptionsLoadCts == myCts) IsLoadingSubscriptions = false;
                    firstItem = false;
                }
                NotifyStateChanged();
            }

            if (!ct.IsCancellationRequested && _subscriptionsLoadCts == myCts && State.SelectedTopicName == topicName)
            {
                _loadedSubscriptionsForTopic = topicName;
                var subsToRemove = SubscriptionDict.Keys.Where(k => !seenSubs.Contains(k)).ToList();
                foreach (var key in subsToRemove)
                {
                    SubscriptionDict.Remove(key);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_subscriptionsLoadCts == myCts)
            {
                ErrorMessage = $"Error loading subscriptions: {ex.Message}";
            }
        }
        finally
        {
            if (_subscriptionsLoadCts == myCts)
            {
                IsLoadingSubscriptions = false;
            }
            NotifyStateChanged();
        }
    }

    private async Task LoadConnectionStringEntitiesAsync(NamespaceConnection connection)
    {
        IsLoadingEntities = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            QueueDict.Clear();
            TopicDict.Clear();

            foreach (var qName in connection.ConfiguredQueues)
            {
                QueueDict[qName] = new ServiceBusQueueInfo
                {
                    Name = qName,
                    Status = "Active",
                    ActiveMessageCount = 0,
                    DeadLetterMessageCount = 0,
                    ScheduledMessageCount = 0,
                    SizeInBytes = 0,
                    RequiresSession = false
                };
            }

            foreach (var tTopic in connection.ConfiguredTopics)
            {
                TopicDict[tTopic.TopicName] = new ServiceBusTopicInfo
                {
                    Name = tTopic.TopicName,
                    Status = "Active",
                    SubscriptionCount = tTopic.Subscriptions.Count
                };
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load manual entities: {ex.Message}";
        }
        finally
        {
            IsLoadingEntities = false;
            NotifyStateChanged();
        }
    }

    public async Task RegisterQueueAsync(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        queueName = queueName.Trim();
        if (!connection.ConfiguredQueues.Contains(queueName))
        {
            connection.ConfiguredQueues.Add(queueName);
            await _navState.AddNamespaceConnectionAsync(connection);
            QueueDict[queueName] = new ServiceBusQueueInfo
            {
                Name = queueName,
                Status = "Active",
                ActiveMessageCount = 0,
                DeadLetterMessageCount = 0,
                ScheduledMessageCount = 0,
                SizeInBytes = 0,
                RequiresSession = false
            };
            NotifyStateChanged();
            _notificationService.NotifySuccess($"Queue '{queueName}' registered successfully.");
        }
        else
        {
            _notificationService.NotifyError($"Queue '{queueName}' is already registered.");
        }
    }

    public async Task RegisterTopicAsync(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        topicName = topicName.Trim();
        if (!connection.ConfiguredTopics.Any(t => t.TopicName == topicName))
        {
            connection.ConfiguredTopics.Add(new ConfiguredTopic { TopicName = topicName });
            await _navState.AddNamespaceConnectionAsync(connection);
            TopicDict[topicName] = new ServiceBusTopicInfo
            {
                Name = topicName,
                Status = "Active",
                SubscriptionCount = 0
            };
            NotifyStateChanged();
            _notificationService.NotifySuccess($"Topic '{topicName}' registered successfully.");
        }
        else
        {
            _notificationService.NotifyError($"Topic '{topicName}' is already registered.");
        }
    }

    public async Task RegisterSubscriptionAsync(string topicName, string subscriptionName)
    {
        if (string.IsNullOrWhiteSpace(topicName) || string.IsNullOrWhiteSpace(subscriptionName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        subscriptionName = subscriptionName.Trim();
        var topic = connection.ConfiguredTopics.FirstOrDefault(t => t.TopicName == topicName);
        if (topic == null) return;

        if (!topic.Subscriptions.Contains(subscriptionName))
        {
            topic.Subscriptions.Add(subscriptionName);
            await _navState.AddNamespaceConnectionAsync(connection);
            if (State.SelectedTopicName == topicName)
            {
                SubscriptionDict[subscriptionName] = new ServiceBusSubscriptionInfo
                {
                    Name = subscriptionName,
                    Status = "Active",
                    ActiveMessageCount = 0,
                    DeadLetterMessageCount = 0,
                    RequiresSession = false
                };
            }
            if (TopicDict.TryGetValue(topicName, out var topicInfo))
            {
                TopicDict[topicName] = topicInfo with { SubscriptionCount = topic.Subscriptions.Count };
            }
            NotifyStateChanged();
            _notificationService.NotifySuccess($"Subscription '{subscriptionName}' registered successfully.");
        }
        else
        {
            _notificationService.NotifyError($"Subscription '{subscriptionName}' is already registered.");
        }
    }

    public void UnregisterQueue(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        _confirmModal.Show(
            title: "Forget Queue",
            message: $"Are you sure you want to forget the queue '{queueName}'?",
            detail: "This will only remove it from Bussin's local preferences. The actual queue on the broker will not be affected.",
            confirmText: "Forget Queue",
            confirmClass: "btn-danger",
            onConfirm: async () =>
            {
                if (connection.ConfiguredQueues.Contains(queueName))
                {
                    connection.ConfiguredQueues.Remove(queueName);
                    await _navState.AddNamespaceConnectionAsync(connection);
                    QueueDict.Remove(queueName);
                    if (State.SelectedQueueName == queueName)
                    {
                        State.ClearSelection();
                    }
                    NotifyStateChanged();
                    _notificationService.NotifySuccess($"Queue '{queueName}' forgotten successfully.");
                }
            }
        );
    }

    public void UnregisterTopic(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        _confirmModal.Show(
            title: "Forget Topic",
            message: $"Are you sure you want to forget the topic '{topicName}'?",
            detail: "This will remove the topic and its subscriptions from Bussin's local preferences. The actual topic on the broker will not be affected.",
            confirmText: "Forget Topic",
            confirmClass: "btn-danger",
            onConfirm: async () =>
            {
                var topic = connection.ConfiguredTopics.FirstOrDefault(t => t.TopicName == topicName);
                if (topic != null)
                {
                    connection.ConfiguredTopics.Remove(topic);
                    await _navState.AddNamespaceConnectionAsync(connection);
                    TopicDict.Remove(topicName);
                    if (State.SelectedTopicName == topicName)
                    {
                        State.ClearSelection();
                        SubscriptionDict.Clear();
                    }
                    NotifyStateChanged();
                    _notificationService.NotifySuccess($"Topic '{topicName}' forgotten successfully.");
                }
            }
        );
    }

    public void UnregisterSubscription(string topicName, string subscriptionName)
    {
        if (string.IsNullOrWhiteSpace(topicName) || string.IsNullOrWhiteSpace(subscriptionName) || State.CurrentNamespace == null) return;
        var connection = _navState.GetNamespaceConnection(State.CurrentNamespace.FullyQualifiedNamespace);
        if (connection == null) return;

        _confirmModal.Show(
            title: "Forget Subscription",
            message: $"Are you sure you want to forget the subscription '{subscriptionName}'?",
            detail: $"This will only remove the subscription from this topic in Bussin's local preferences. The actual subscription on the broker will not be affected.",
            confirmText: "Forget Subscription",
            confirmClass: "btn-danger",
            onConfirm: async () =>
            {
                var topic = connection.ConfiguredTopics.FirstOrDefault(t => t.TopicName == topicName);
                if (topic != null && topic.Subscriptions.Contains(subscriptionName))
                {
                    topic.Subscriptions.Remove(subscriptionName);
                    await _navState.AddNamespaceConnectionAsync(connection);
                    if (State.SelectedTopicName == topicName)
                    {
                        SubscriptionDict.Remove(subscriptionName);
                        if (State.SelectedSubscriptionName == subscriptionName)
                        {
                            await SelectTopic(topicName);
                        }
                    }
                    if (TopicDict.TryGetValue(topicName, out var topicInfo))
                    {
                        TopicDict[topicName] = topicInfo with { SubscriptionCount = topic.Subscriptions.Count };
                    }
                    NotifyStateChanged();
                    _notificationService.NotifySuccess($"Subscription '{subscriptionName}' forgotten successfully.");
                }
            }
        );
    }

    public string GetEntityPath() => State.GetEntityPath();

    public void Dispose()
    {
        _entitiesLoadCts?.Cancel();
        _subscriptionsLoadCts?.Cancel();
    }
}
