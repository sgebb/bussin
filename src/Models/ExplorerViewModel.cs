using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Azure.Core;
using Bussin.Services;
using Bussin.Services.Demo;
using Bussin.Components;
using System.Linq;

namespace Bussin.Models;

/// <summary>
/// ViewModel for the Explorer page to encapsulate state and logic, 
/// separating it from the UI rendering in Explorer.razor.
/// </summary>
public sealed class ExplorerViewModel : IDisposable
{
    private readonly IAuthenticationService _authService;
    private readonly IAzureResourceService _resourceService;
    private readonly IServiceBusOperationsService _operationsService;
    private readonly IServiceBusJsInteropService _jsInterop;
    private readonly INotificationService _notificationService;
    private readonly IConfirmModalService _confirmModal;
    private readonly BackgroundPurgeService _backgroundPurge;
    private readonly BackgroundSearchService _backgroundSearch;
    private readonly BackgroundResubmitService _backgroundResubmit;
    private readonly NavigationStateService _navState;
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigation;

    public ExplorerViewModel(
        IAuthenticationService authService,
        IAzureResourceService resourceService,
        IServiceBusOperationsService operationsService,
        IServiceBusJsInteropService jsInterop,
        INotificationService notificationService,
        IConfirmModalService confirmModal,
        BackgroundPurgeService backgroundPurge,
        BackgroundSearchService backgroundSearch,
        BackgroundResubmitService backgroundResubmit,
        NavigationStateService navState,
        IJSRuntime jsRuntime,
        NavigationManager navigation)
    {
        _authService = authService;
        _resourceService = resourceService;
        _operationsService = operationsService;
        _jsInterop = jsInterop;
        _notificationService = notificationService;
        _confirmModal = confirmModal;
        _backgroundPurge = backgroundPurge;
        _backgroundSearch = backgroundSearch;
        _backgroundResubmit = backgroundResubmit;
        _navState = navState;
        _jsRuntime = jsRuntime;
        _navigation = navigation;

        // Initialize state listeners
        _confirmModal.OnChange += NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged += NotifyStateChanged;
        _backgroundPurge.OnPurgeCompleted += HandlePurgeCompleted;
        _backgroundResubmit.OnOperationsChanged += NotifyStateChanged;
        _backgroundSearch.OnViewResultsRequested += OnViewSearchResultsRequested;
    }

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    // Callbacks to UI
    public Func<Task>? OnClearSelectionRequested { get; set; }
    public Func<Task>? OnFocusRenameInputRequested { get; set; }

    // State
    public ExplorerState State { get; } = new();
    public Dictionary<string, ServiceBusQueueInfo> QueueDict { get; } = new();
    public Dictionary<string, ServiceBusTopicInfo> TopicDict { get; } = new();
    public Dictionary<string, ServiceBusSubscriptionInfo> SubscriptionDict { get; } = new();

    public List<ServiceBusMessage> PeekedMessages { get; set; } = new();
    public bool HasPeeked { get; set; }
    public int PeekCount { get; set; } = 50;
    public int PeekFromSequence { get; set; } = 0;
    public PeekOptions? LastPeekOptions { get; set; }

    public string DisplayName { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? RawErrorMessage { get; set; }
    public bool IsConnectionStringMode { get; set; }
    
    // UI Visibility State
    public bool IsLoadingEntities { get; set; }
    public bool IsLoadingSubscriptions { get; set; }
    public bool IsPeeking { get; set; }
    public bool IsSending { get; set; }
    public bool IsProcessingBatch { get; set; }
    
    public bool ShowSendMessageModal { get; set; }
    public bool ShowMessageDetailModal { get; set; }
    public bool ShowPeekLockModal { get; set; }
    public bool ShowMonitorModal { get; set; }
    public bool ShowReceiveAndLockModal { get; set; }
    public bool ShowPeekOptionsModal { get; set; }
    public bool ShowStatsModal { get; set; }
    public bool ShowExportOptionsModal { get; set; }
    public Bussin.Components.ExportOptionsModal.ExportOptions MessageExportOptions { get; set; } = new();
    
    public ServiceBusMessage? SelectedMessage { get; set; }
    public int PeekLockCount { get; set; } = 10;
    public int PeekLockDuration { get; set; } = 30;

    // Rename state
    public bool IsEditingDisplayName { get; set; }
    public string EditDisplayName { get; set; } = "";
    
    // Resubmit Modal state
    public bool ResubmitRemoveFromDLQ { get; set; } = true;
    public bool IsResubmitModal { get; set; }

    private CancellationTokenSource? _loadCts;
    private string? _loadedSubscriptionsForTopic;
    private string _currentExportMode = "";
    private List<long> _currentExportSequenceNumbers = new();

    // Computed
    public List<ServiceBusQueueInfo> Queues => QueueDict.Values.OrderBy(q => q.Name).ToList();
    public List<ServiceBusTopicInfo> Topics => TopicDict.Values.OrderBy(t => t.Name).ToList();
    public List<ServiceBusSubscriptionInfo> Subscriptions => SubscriptionDict.Values.OrderBy(s => s.Name).ToList();
    
    public string NamespaceNameOnly => State.NamespaceNameOnly;
    public bool HasEntitySelected => State.HasEntitySelected;
    public bool HasQueueOrSubscriptionSelected => State.HasQueueOrSubscriptionSelected;
    public bool CanSend => HasEntitySelected && !State.IsViewingDLQ;
    public bool SelectedEntityRequiresSession =>
        (State.SelectedQueueName != null && QueueDict.TryGetValue(State.SelectedQueueName, out var q) && q.RequiresSession) ||
        (State.SelectedSubscriptionName != null && State.SelectedSubscriptionName != null && SubscriptionDict.TryGetValue(State.SelectedSubscriptionName, out var s) && s.RequiresSession);

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

    public PurgeOperation? CurrentPurgeOperation
    {
        get
        {
            var currentEntity = State.GetEntityPath();
            var currentDLQ = State.IsViewingDLQ;
            return _backgroundPurge.ActiveOperations.FirstOrDefault(op =>
                op.EntityPath == currentEntity && op.IsDeadLetter == currentDLQ);
        }
    }

    public ResubmitOperation? CurrentResubmitOperation
    {
        get
        {
            var currentEntity = State.GetEntityPath();
            return _backgroundResubmit.ActiveOperations.FirstOrDefault(op =>
                op.EntityPath == currentEntity);
        }
    }

    public bool ArePeekOptionsModified => LastPeekOptions != null && (
        LastPeekOptions.MaxCount != 50 ||
        LastPeekOptions.FromSequenceNumber != 0 ||
        LastPeekOptions.HasActiveFilter ||
        LastPeekOptions.PeekFromNewest
    );

    // ConfirmModal Bridge
    public bool ConfirmModalVisible => _confirmModal.IsVisible;
    public string ConfirmModalTitle => _confirmModal.Title;
    public string ConfirmModalMessage => _confirmModal.Message;
    public string ConfirmModalDetailMessage => _confirmModal.DetailMessage;
    public string ConfirmModalConfirmButtonText => _confirmModal.ConfirmButtonText;
    public string ConfirmModalConfirmButtonClass => _confirmModal.ConfirmButtonClass;
    public string ConfirmModalAlternativeButtonText => _confirmModal.AlternativeButtonText;
    public string ConfirmModalAlternativeButtonClass => _confirmModal.AlternativeButtonClass;
    public bool ConfirmModalIsProcessing { get => _confirmModal.IsProcessing; set => _confirmModal.IsProcessing = value; }
    public int? ConfirmModalProgressCount => _confirmModal.ProgressCount;

    public Task OnConfirmModalConfirmAsync() => _confirmModal.ConfirmAsync();
    public Task OnConfirmModalAlternativeConfirmAsync() => _confirmModal.AlternativeConfirmAsync();
    public Task OnConfirmModalCancelAsync() => _confirmModal.CancelAsync();
    public Task<bool> IsAuthenticatedAsync() => _authService.IsAuthenticatedAsync();

    public async Task InitializeAsync(string? namespaceParam, string? resourceGroupParam, string? subscriptionIdParam, string? nameParam)
    {
        if (!string.IsNullOrEmpty(namespaceParam))
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

            PeekedMessages.Clear();
            HasPeeked = false;
            PeekFromSequence = 0;

            QueueDict.Clear();
            TopicDict.Clear();
            SubscriptionDict.Clear();

            DisplayName = connection?.DisplayName ?? nameParam ?? "";

            if (IsConnectionStringMode)
            {
                await LoadConnectionStringEntitiesAsync(connection!);
            }
            else
            {
                // Only load if we have the minimum required information for the ResourceIdentifier
                if (!string.IsNullOrEmpty(State.CurrentNamespace?.SubscriptionId) && 
                    !string.IsNullOrEmpty(State.CurrentNamespace?.ResourceGroup))
                {
                    await LoadEntitiesAsync();
                }
            }
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

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        IsLoadingEntities = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            var credential = await _authService.GetTokenCredentialAsync();
            if (credential == null)
            {
                ErrorMessage = "Failed to get authentication token";
                IsLoadingEntities = false;
                return;
            }

            var seenQueues = new System.Collections.Generic.HashSet<string>();
            var seenTopics = new System.Collections.Generic.HashSet<string>();

            await Task.WhenAll(
                LoadQueuesAsync(credential, State.CurrentNamespace, seenQueues),
                LoadTopicsAsync(credential, State.CurrentNamespace, seenTopics)
            );

            // Clean up deleted queues and topics that were not returned by Azure
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
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load entities: {ex.Message}";
        }
        finally
        {
            IsLoadingEntities = false;
            NotifyStateChanged();
        }
    }

    private async Task LoadQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, System.Collections.Generic.HashSet<string> seenQueues)
    {
        var ct = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            await foreach (var queue in _resourceService.ListQueuesAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                QueueDict[queue.Name] = queue;
                seenQueues.Add(queue.Name);
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, System.Collections.Generic.HashSet<string> seenTopics)
    {
        var ct = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            await foreach (var topic in _resourceService.ListTopicsAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                TopicDict[topic.Name] = topic;
                seenTopics.Add(topic.Name);
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshEntitiesAsync()
    {
        // Do NOT clear dictionaries! This prevents the UI from flashing/emptying.
        await LoadEntitiesAsync();

        if (State.SelectedTopicName != null)
        {
            await LoadSubscriptionsAsync(State.SelectedTopicName);
        }

        _notificationService.NotifySuccess("Refreshed queues and topics");
    }

    public void SelectQueue(string queueName)
    {
        State.SelectQueue(queueName);
        ResetMessageState();
        NotifyStateChanged();
    }

    public async Task SelectTopicAsync(string topicName)
    {
        State.SelectTopic(topicName);
        ResetMessageState();
        State.IsViewingDLQ = false;

        if (_loadedSubscriptionsForTopic != topicName)
        {
            SubscriptionDict.Clear();
            _loadedSubscriptionsForTopic = null;
        }

        await LoadSubscriptionsAsync(topicName);
        NotifyStateChanged();
    }

    public void SelectSubscription(string subName)
    {
        State.SelectSubscription(subName);
        ResetMessageState();
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

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoadingSubscriptions = true;
        NotifyStateChanged();

        if (_loadedSubscriptionsForTopic != topicName)
        {
            SubscriptionDict.Clear();
        }

        try
        {
            var credential = await _authService.GetTokenCredentialAsync();
            if (credential == null)
            {
                IsLoadingSubscriptions = false;
                return;
            }

            var seenSubs = new System.Collections.Generic.HashSet<string>();
            var firstItem = true;
            await foreach (var sub in _resourceService.ListSubscriptionsAsync(credential, State.CurrentNamespace, topicName, ct))
            {
                if (ct.IsCancellationRequested) break;

                if (State.SelectedTopicName == topicName)
                {
                    SubscriptionDict[sub.Name] = sub;
                    seenSubs.Add(sub.Name);
                }

                if (firstItem)
                {
                    IsLoadingSubscriptions = false;
                    firstItem = false;
                }
                NotifyStateChanged();
            }

            if (!ct.IsCancellationRequested && State.SelectedTopicName == topicName)
            {
                _loadedSubscriptionsForTopic = topicName;

                // Remove subscriptions that are no longer present
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
            ErrorMessage = $"Error loading subscriptions: {ex.Message}";
        }
        finally
        {
            IsLoadingSubscriptions = false;
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
                        ResetMessageState();
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
                        ResetMessageState();
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
                            State.SelectTopic(topicName);
                            ResetMessageState();
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

    public void ResetMessageState()
    {
        PeekedMessages.Clear();
        HasPeeked = false;
        PeekFromSequence = 0;
        LastPeekOptions = null;
        OnClearSelectionRequested?.Invoke();
    }

    public void ClearPeekedMessages()
    {
        PeekedMessages.Clear();
        HasPeeked = false;
        PeekFromSequence = 0;
        LastPeekOptions = null;
        OnClearSelectionRequested?.Invoke();
        NotifyStateChanged();
    }

    public async Task PeekMessagesAsync(bool append = false)
    {
        if (append && LastPeekOptions != null)
        {
            await PeekWithOptionsAsync(LastPeekOptions, append: true);
            return;
        }

        if (append && PeekedMessages.Count >= 1000)
        {
            ErrorMessage = "Maximum of 1000 messages loaded. Use filters or start from a different sequence number.";
            return;
        }

        IsPeeking = true;
        ErrorMessage = null;
        NotifyStateChanged();

        if (!append)
        {
            HasPeeked = true;
            PeekedMessages.Clear();
            PeekFromSequence = 0;
            LastPeekOptions = null;
        }
        else if (PeekedMessages.Any())
        {
            PeekFromSequence = (int)(PeekedMessages.Max(m => m.SequenceNumber ?? 0) + 1);
        }

        try
        {
            var token = await GetTokenAsync(GetEntityPath());
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Service Bus token not available";
                return;
            }

            var messages = await PeekEntityMessagesInternalAsync(token);

            if (append)
                PeekedMessages.AddRange(messages);
            else
                PeekedMessages = messages;

            if (messages.Count > 0)
            {
                var suffix = append ? " more" : "";
                _notificationService.NotifySuccess($"Peeked {messages.Count}{suffix} messages", "peek-result");
            }
            else
            {
                _notificationService.NotifyInfo(append ? "No more messages available." : "No messages found", "peek-result");
            }
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message.Replace("Peek failed: Peek failed:", "Peek failed:"), "peek messages from");
        }
        finally
        {
            IsPeeking = false;
            NotifyStateChanged();
        }
    }

    private async Task<List<ServiceBusMessage>> PeekEntityMessagesInternalAsync(string token)
    {
        if (State.SelectedQueueName != null)
        {
            return await _jsInterop.PeekQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName, token, PeekCount, PeekFromSequence, State.IsViewingDLQ);
        }
        if (State.SelectedTopicName != null && State.SelectedSubscriptionName != null)
        {
            return await _jsInterop.PeekSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName, State.SelectedSubscriptionName, token, PeekCount, PeekFromSequence, State.IsViewingDLQ);
        }
        return new List<ServiceBusMessage>();
    }

    public async Task PeekWithOptionsAsync(PeekOptions options, bool append = false)
    {
        ShowPeekOptionsModal = false;
        IsPeeking = true;
        ErrorMessage = null;
        NotifyStateChanged();

        if (!append)
        {
            HasPeeked = true;
            PeekedMessages.Clear();
            LastPeekOptions = options;
        }

        try
        {
            var token = await GetTokenAsync(GetEntityPath());
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Service Bus token not available";
                return;
            }

            long startSequence = options.FromSequenceNumber;
            if (append)
            {
                if (options.PeekFromNewest && PeekedMessages.Any())
                {
                    var oldestSeq = PeekedMessages.Min(m => m.SequenceNumber ?? 0);
                    startSequence = Math.Max(0, oldestSeq - options.MaxCount);
                }
                else
                {
                    startSequence = PeekFromSequence;
                }
            }
            else if (options.PeekFromNewest)
            {
                startSequence = await CalculateNewestStartSequenceAsync(token, options.MaxCount);
            }

            List<ServiceBusMessage> messages;
            if (options.HasActiveFilter)
            {
                var result = await PeekWithFiltersAsync(token, options, startSequence);
                messages = result.messages;
                PeekFromSequence = (int)result.nextSequence;
            }
            else
            {
                messages = await PeekIterativeAsync(token, options.MaxCount, startSequence);
                if (messages.Any())
                {
                    PeekFromSequence = (int)(messages.Max(m => m.SequenceNumber ?? 0) + 1);
                }
            }

            if (options.PeekFromNewest)
            {
                messages = messages.OrderByDescending(m => m.SequenceNumber ?? 0).ToList();
            }

            if (append)
            {
                var existingSequences = PeekedMessages.Where(m => m.SequenceNumber.HasValue).Select(m => m.SequenceNumber!.Value).ToHashSet();
                var newMessages = messages.Where(m => !m.SequenceNumber.HasValue || !existingSequences.Contains(m.SequenceNumber.Value)).ToList();
                PeekedMessages.AddRange(newMessages);
                messages = newMessages;
            }
            else
            {
                PeekedMessages = messages;
            }

            if (messages.Count > 0)
            {
                var filterMsg = options.HasActiveFilter ? $" matching filters" : "";
                var suffix = append ? " more" : "";
                var entityType = State.IsQueueSelected ? "queue" : "subscription";
                var entityName = State.GetEntityPath();
                var source = State.IsViewingDLQ ? $"DLQ of {entityType} '{entityName}'" : $"{entityType} '{entityName}'";
                _notificationService.NotifySuccess($"Peeked {messages.Count}{suffix} messages from {source}{filterMsg}", "peek-result");
            }
            else
            {
                var entityType = State.IsQueueSelected ? "queue" : "subscription";
                var entityName = State.GetEntityPath();
                var source = State.IsViewingDLQ ? $"DLQ of {entityType} '{entityName}'" : $"{entityType} '{entityName}'";
                _notificationService.NotifyInfo(append ? $"No more messages available from {source}" : $"No messages found in {source}", "peek-result");
            }
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message.Replace("Peek failed: Peek failed:", "Peek failed:"), "peek messages from");
        }
        finally
        {
            IsPeeking = false;
            NotifyStateChanged();
        }
    }

    private async Task<List<ServiceBusMessage>> PeekIterativeAsync(string token, int desiredCount, long startSequence)
    {
        const int batchSize = 250;
        var allMessages = new List<ServiceBusMessage>();
        var currentSequence = startSequence;
        
        while (allMessages.Count < desiredCount)
        {
            var remaining = desiredCount - allMessages.Count;
            var fetchCount = Math.Min(remaining, batchSize);
            List<ServiceBusMessage> batch;
            
            if (State.SelectedQueueName != null)
                batch = await _jsInterop.PeekQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName, token, fetchCount, (int)currentSequence, State.IsViewingDLQ);
            else if (State.IsSubscriptionSelected)
                batch = await _jsInterop.PeekSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, token, fetchCount, (int)currentSequence, State.IsViewingDLQ);
            else
                break;
            
            if (batch.Count == 0) break;
            allMessages.AddRange(batch);
            var maxSeq = batch.Max(m => m.SequenceNumber ?? 0);
            currentSequence = maxSeq + 1;
            if (batch.Count < fetchCount) break;
        }
        return allMessages;
    }

    private async Task<long> CalculateNewestStartSequenceAsync(string token, int maxCount)
    {
        try
        {
            List<ServiceBusMessage> firstMessage;
            if (State.SelectedQueueName != null)
                firstMessage = await _jsInterop.PeekQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName, token, 1, 0, State.IsViewingDLQ);
            else if (State.IsSubscriptionSelected)
                firstMessage = await _jsInterop.PeekSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, token, 1, 0, State.IsViewingDLQ);
            else
                return 0;

            if (!firstMessage.Any()) return 0;
            var firstSeq = firstMessage[0].SequenceNumber ?? 0;
            long totalMessages = CurrentEntityMessageCount;
            if (totalMessages <= maxCount) return 0;
            return Math.Max(0, firstSeq + (totalMessages - maxCount));
        }
        catch { return 0; }
    }

    private async Task<(List<ServiceBusMessage> messages, long nextSequence)> PeekWithFiltersAsync(string token, PeekOptions options, long startSequence)
    {
        var matchingMessages = new List<ServiceBusMessage>();
        var currentSequence = (int)startSequence;
        const int batchSize = 100;
        const int maxAttempts = 100;
        var attempts = 0;

        while (matchingMessages.Count < 50 && attempts < maxAttempts)
        {
            List<ServiceBusMessage> batch;
            if (State.SelectedQueueName != null)
                batch = await _jsInterop.PeekQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName, token, batchSize, currentSequence, State.IsViewingDLQ);
            else if (State.IsSubscriptionSelected)
                batch = await _jsInterop.PeekSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, token, batchSize, currentSequence, State.IsViewingDLQ);
            else
                break;

            if (!batch.Any()) break;
            var matches = batch.Where(m =>
            {
                if (!string.IsNullOrWhiteSpace(options.BodyFilter) && m.Body?.Contains(options.BodyFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                if (!string.IsNullOrWhiteSpace(options.MessageIdFilter) && m.MessageId?.Contains(options.MessageIdFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                if (!string.IsNullOrWhiteSpace(options.SubjectFilter) && m.Subject?.Contains(options.SubjectFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                return true;
            }).ToList();

            matchingMessages.AddRange(matches);
            currentSequence = (int)(batch.Max(m => m.SequenceNumber ?? 0) + 1);
            attempts++;
        }
        return (matchingMessages.Take(50).ToList(), currentSequence);
    }

    public void PurgeMessages()
    {
        var entityName = State.GetEntityPath();
        var dlqSuffix = State.IsViewingDLQ ? " (Dead Letter Queue)" : "";
        _confirmModal.Show(
            title: "Confirm Purge",
            message: $"Are you sure you want to purge all messages from {entityName}{dlqSuffix}?",
            detail: "This action cannot be undone. The purge operation runs in the background and might take a while to complete.",
            confirmText: "Purge",
            confirmClass: "btn-danger",
            onConfirm: ExecutePurgeAsync
        );
    }

    private async Task ExecutePurgeAsync()
    {
        _confirmModal.IsProcessing = true;
        ErrorMessage = null;
        try
        {
            var entityType = State.IsQueueSelected ? "queue" : "subscription";
            var entityPath = State.GetEntityPath();
            await _backgroundPurge.StartPurgeAsync(State.FullyQualifiedNamespace, entityType, entityPath, State.IsViewingDLQ);
            PeekedMessages.Clear();
            PeekFromSequence = 0;
            _confirmModal.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to purge: {ex.Message}";
        }
        finally
        {
            _confirmModal.IsProcessing = false;
            NotifyStateChanged();
        }
    }

    public void ResubmitAllMessages()
    {
        if (!State.IsViewingDLQ) return;
        ResubmitRemoveFromDLQ = true;
        IsResubmitModal = true;
        var entityName = State.GetEntityPath();
        _confirmModal.Show(
            title: "Confirm Resubmit All",
            message: $"Resubmit ALL messages from Dead Letter Queue?",
            detail: "This will resubmit all messages from the DLQ back to the main queue/subscription.",
            confirmText: "Resubmit All",
            confirmClass: "btn-warning",
            onConfirm: ExecuteResubmitAllAsync
        );
    }

    private async Task ExecuteResubmitAllAsync()
    {
        _confirmModal.IsProcessing = true;
        ErrorMessage = null;
        try
        {
            var entityType = State.IsQueueSelected ? "queue" : "subscription";
            var entityPath = State.GetEntityPath();
            await _backgroundResubmit.StartResubmitAsync(State.FullyQualifiedNamespace, entityType, entityPath, ResubmitRemoveFromDLQ);
            PeekedMessages.Clear();
            PeekFromSequence = 0;
            _confirmModal.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start resubmit: {ex.Message}";
        }
        finally
        {
            _confirmModal.IsProcessing = false;
            IsResubmitModal = false;
            NotifyStateChanged();
        }
    }

    public async Task SendMessageAsync(SendMessageModal.SendMessageRequest request)
    {
        IsSending = true;
        ErrorMessage = null;
        try
        {
            var props = new MessageProperties
            {
                MessageId = request.MessageId,
                CorrelationId = request.CorrelationId,
                Subject = request.Subject,
                ReplyTo = request.ReplyTo,
                To = request.To,
                SessionId = request.SessionId,
                ContentType = request.ContentType,
                TimeToLive = request.TimeToLiveSeconds.HasValue ? TimeSpan.FromSeconds(request.TimeToLiveSeconds.Value) : null,
                ApplicationProperties = request.CustomProperties,
                MessageAnnotations = request.ScheduledEnqueueTimeUtc.HasValue ? new Dictionary<string, object> { ["x-opt-scheduled-enqueue-time"] = DateTime.SpecifyKind(request.ScheduledEnqueueTimeUtc.Value, DateTimeKind.Utc) } : null
            };

            if (State.SelectedQueueName != null)
                await _operationsService.SendQueueMessageAsync(NamespaceNameOnly, State.SelectedQueueName, request.Body, props);
            else if (State.SelectedTopicName != null)
                await _operationsService.SendTopicMessageAsync(NamespaceNameOnly, State.SelectedTopicName, request.Body, props);

            ShowSendMessageModal = false;
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message.Replace("Send failed: Send failed:", "Send failed:"), "send messages to");
        }
        finally
        {
            IsSending = false;
            NotifyStateChanged();
        }
    }

    public async Task EditAndResubmitMessageAsync(Bussin.Components.MessageDetailModal.EditResubmitRequest request)
    {
        IsProcessingBatch = true;
        ErrorMessage = null;
        try
        {
            var props = new MessageProperties
            {
                MessageId = request.MessageId,
                CorrelationId = request.CorrelationId,
                Subject = request.Subject,
                ReplyTo = request.ReplyTo,
                To = request.To,
                SessionId = request.SessionId,
                ContentType = request.ContentType,
                ApplicationProperties = request.CustomProperties,
                PartitionKey = request.PartitionKey
            };

            // 1. Send the edited message back to the active queue/topic
            if (State.IsQueueSelected)
                await _operationsService.SendQueueMessageAsync(NamespaceNameOnly, State.SelectedQueueName!, request.Body, props);
            else if (State.IsSubscriptionSelected)
                await _operationsService.SendTopicMessageAsync(NamespaceNameOnly, State.SelectedTopicName!, request.Body, props);

            // 2. If it's a DLQ message and the user checked "delete original", delete it
            if (State.IsViewingDLQ && request.DeleteOriginal)
            {
                if (State.IsQueueSelected)
                {
                    await _operationsService.DeleteQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName!, new[] { request.OriginalSequenceNumber }, fromDeadLetter: true);
                }
                else if (State.IsSubscriptionSelected)
                {
                    await _operationsService.DeleteSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, new[] { request.OriginalSequenceNumber }, fromDeadLetter: true);
                }

                // Remove it from the local list
                PeekedMessages.RemoveAll(m => m.SequenceNumber == request.OriginalSequenceNumber);
            }

            ShowMessageDetailModal = false;
            SelectedMessage = null;
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message, "resubmit edited message for");
        }
        finally
        {
            IsProcessingBatch = false;
            NotifyStateChanged();
        }
    }

    public async Task SendBatchMessagesAsync(List<SendMessageModal.SendMessageRequest> requests)
    {
        IsSending = true;
        ErrorMessage = null;
        try
        {
            var batchObjects = requests.Select(req => new { 
                body = req.Body, 
                properties = new MessageProperties {
                    MessageId = req.MessageId,
                    CorrelationId = req.CorrelationId,
                    Subject = req.Subject,
                    ReplyTo = req.ReplyTo,
                    To = req.To,
                    SessionId = req.SessionId,
                    ContentType = req.ContentType,
                    TimeToLive = req.TimeToLiveSeconds.HasValue ? TimeSpan.FromSeconds(req.TimeToLiveSeconds.Value) : null,
                    ApplicationProperties = req.CustomProperties,
                    MessageAnnotations = req.ScheduledEnqueueTimeUtc.HasValue ? new Dictionary<string, object> { ["x-opt-scheduled-enqueue-time"] = DateTime.SpecifyKind(req.ScheduledEnqueueTimeUtc.Value, DateTimeKind.Utc) } : null
                }
            }).Cast<object>().ToList();

            if (State.SelectedQueueName != null)
                await _operationsService.SendQueueMessagesBatchAsync(NamespaceNameOnly, State.SelectedQueueName, batchObjects);
            else if (State.SelectedTopicName != null)
                await _operationsService.SendTopicMessagesBatchAsync(NamespaceNameOnly, State.SelectedTopicName, batchObjects);

            ShowSendMessageModal = false;
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message.Replace("Batch send failed: Batch send failed:", "Batch send failed:"), "send messages to");
        }
        finally
        {
            IsSending = false;
            NotifyStateChanged();
        }
    }

    public void ToggleDLQ()
    {
        State.IsViewingDLQ = !State.IsViewingDLQ;
        PeekedMessages.Clear();
        PeekFromSequence = 0;
        HasPeeked = false;
        NotifyStateChanged();
    }

    private void OnViewSearchResultsRequested(SearchOperation operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (operation.MatchingSequenceNumbers.Count == 0) return;
                bool match = false;
                if (operation.EntityType == "queue" && State.SelectedQueueName == operation.EntityPath) match = true;
                if (operation.EntityType == "subscription" && State.SelectedTopicName == operation.TopicName && State.SelectedSubscriptionName == operation.SubscriptionName) match = true;
                if (!match || operation.IsDeadLetter != State.IsViewingDLQ) return;

                IsPeeking = true;
                NotifyStateChanged();
                string entityPath;
                if (operation.EntityType == "queue")
                {
                    entityPath = operation.IsDeadLetter ? $"{operation.EntityPath}/$DeadLetterQueue" : operation.EntityPath;
                }
                else
                {
                    var subPath = $"{operation.TopicName}/subscriptions/{operation.SubscriptionName}";
                    entityPath = operation.IsDeadLetter ? $"{subPath}/$DeadLetterQueue" : subPath;
                }
                var token = await GetTokenAsync(entityPath);
                if (string.IsNullOrEmpty(token)) return;

                List<ServiceBusMessage> loadedMessages;
                if (operation.EntityType == "queue")
                    loadedMessages = await _jsInterop.PeekQueueMessagesBySequenceAsync(operation.NamespaceName, operation.EntityPath, token, operation.MatchingSequenceNumbers.ToArray(), operation.IsDeadLetter);
                else
                    loadedMessages = await _jsInterop.PeekSubscriptionMessagesBySequenceAsync(operation.NamespaceName, operation.TopicName!, operation.SubscriptionName!, token, operation.MatchingSequenceNumbers.ToArray(), operation.IsDeadLetter);

                PeekedMessages = loadedMessages;
                HasPeeked = true;
                PeekFromSequence = 0;
                LastPeekOptions = null;
                NotifyStateChanged();
            }
            finally { IsPeeking = false; NotifyStateChanged(); }
        });
    }

    public async Task StartBackgroundSearchAsync(PeekOptions options)
    {
        ShowPeekOptionsModal = false;
        var searchOptions = new BackgroundSearchOptions
        {
            NamespaceName = State.FullyQualifiedNamespace,
            EntityType = State.IsQueueSelected ? "queue" : "subscription",
            EntityPath = State.IsQueueSelected ? State.SelectedQueueName : $"{State.SelectedTopicName}/Subscriptions/{State.SelectedSubscriptionName}",
            TopicName = State.SelectedTopicName,
            SubscriptionName = State.SelectedSubscriptionName,
            IsDeadLetter = State.IsViewingDLQ,
            TotalMessageCount = CurrentEntityMessageCount,
            BodyFilter = options.BodyFilter,
            MessageIdFilter = options.MessageIdFilter,
            SubjectFilter = options.SubjectFilter,
            MaxMatches = options.MaxCount
        };
        await _backgroundSearch.StartSearchAsync(searchOptions);
        _notificationService.NotifySuccess("Background search started.");
    }

    public void DeleteMessages(List<long> sequenceNumbers)
    {
        _confirmModal.Show(
            title: "Confirm Delete",
            message: $"Are you sure you want to delete {sequenceNumbers.Count} message(s)?",
            detail: "This action cannot be undone.",
            confirmText: "Delete Messages",
            confirmClass: "btn-danger",
            onConfirm: () => ExecuteBatchOperationAsync(sequenceNumbers, "delete", async (seqNums) =>
            {
                if (State.IsQueueSelected)
                    await _operationsService.DeleteQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName!, seqNums.ToArray(), State.IsViewingDLQ);
                else if (State.IsSubscriptionSelected)
                    await _operationsService.DeleteSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, seqNums.ToArray(), State.IsViewingDLQ);
            })
        );
        IsResubmitModal = false;
    }

    public void ResubmitMessages(List<long> sequenceNumbers)
    {
        ResubmitRemoveFromDLQ = true;
        IsResubmitModal = true;
        _confirmModal.Show(
            title: "Confirm Resubmit",
            message: $"Resubmit {sequenceNumbers.Count} message(s) from Dead Letter Queue?",
            detail: "Messages will be sent back to the main queue/subscription.",
            confirmText: "Resubmit",
            confirmClass: "btn-primary",
            onConfirm: async () =>
            {
                var shouldRemove = ResubmitRemoveFromDLQ;
                await ExecuteBatchOperationAsync(sequenceNumbers, "resubmit", async (seqNums) =>
                {
                    if (State.IsQueueSelected)
                        await _operationsService.ResendQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName!, seqNums.ToArray(), State.IsViewingDLQ, shouldRemove);
                    else if (State.IsSubscriptionSelected)
                        await _operationsService.ResendSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, seqNums.ToArray(), State.IsViewingDLQ, shouldRemove);
                }, removeFromView: shouldRemove);
            }
        );
    }

    public void MoveToDLQMessages(List<long> sequenceNumbers)
    {
        _confirmModal.Show(
            title: "Confirm Move to Dead Letter Queue",
            message: $"Move {sequenceNumbers.Count} message(s) to Dead Letter Queue?",
            detail: "Messages will be moved to the Dead Letter Queue.",
            confirmText: "Move to Dead Letter Queue",
            confirmClass: "btn-warning",
            onConfirm: () => ExecuteBatchOperationAsync(sequenceNumbers, "move to Dead Letter Queue", async (seqNums) =>
            {
                if (State.IsQueueSelected)
                    await _operationsService.MoveToDLQQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName!, seqNums.ToArray());
                else if (State.IsSubscriptionSelected)
                    await _operationsService.MoveToDLQSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, seqNums.ToArray());
            })
        );
        IsResubmitModal = false;
    }

    private async Task ExecuteBatchOperationAsync(List<long> sequenceNumbers, string operationName, Func<List<long>, Task> operation, bool removeFromView = true)
    {
        _confirmModal.IsProcessing = true;
        ErrorMessage = null;
        try
        {
            await operation(sequenceNumbers);
            if (removeFromView)
            {
                PeekedMessages.RemoveAll(m => m.SequenceNumber.HasValue && sequenceNumbers.Contains(m.SequenceNumber.Value));
                if (PeekedMessages.Count == 0) PeekFromSequence = 0;
            }
            OnClearSelectionRequested?.Invoke();
            _notificationService.NotifySuccess($"{char.ToUpper(operationName[0])}{operationName[1..]} {sequenceNumbers.Count} messages successfully.");
            _confirmModal.Close();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to {operationName} messages: {ex.Message}"; }
        finally { _confirmModal.IsProcessing = false; NotifyStateChanged(); }
    }

    public void StartRename()
    {
        EditDisplayName = DisplayName;
        IsEditingDisplayName = true;
        OnFocusRenameInputRequested?.Invoke();
    }

    public void CancelRename()
    {
        IsEditingDisplayName = false;
        EditDisplayName = "";
    }

    public async Task SaveDisplayNameAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDisplayName) || string.IsNullOrEmpty(State.FullyQualifiedNamespace)) return;
        await _navState.RenameNamespaceAsync(State.FullyQualifiedNamespace, EditDisplayName.Trim());
        DisplayName = EditDisplayName.Trim();
        IsEditingDisplayName = false;
        EditDisplayName = "";
        _notificationService.NotifySuccess("Namespace renamed successfully.");
        NotifyStateChanged();
    }

    public async Task ToggleFavoriteAsync()
    {
        if (string.IsNullOrEmpty(State.FullyQualifiedNamespace)) return;

        if (IsConnectionStringMode)
        {
            var connection = _navState.GetNamespaceConnection(State.FullyQualifiedNamespace);
            if (connection != null)
            {
                _confirmModal.Show(
                    "Remove Favorite Connection",
                    $"Are you sure you want to remove the connection '{DisplayName}'?",
                    "This will remove the connection configuration from your local settings.",
                    "Remove",
                    "btn-danger",
                    async () =>
                    {
                        await _navState.RemoveFromFavoritesAsync(State.FullyQualifiedNamespace);
                        _navigation.NavigateTo("/");
                    });
            }
            return;
        }

        if (_navState.IsFavorite(State.FullyQualifiedNamespace))
            await _navState.RemoveFromFavoritesAsync(State.FullyQualifiedNamespace);
        else
            await _navState.AddToFavoritesAsync(State.FullyQualifiedNamespace, State.CurrentNamespace!.ResourceGroup, State.CurrentNamespace!.SubscriptionId, DisplayName);
        NotifyStateChanged();
    }
    
    public bool IsFavorite => _navState.IsFavorite(State.FullyQualifiedNamespace);

    public void CloseMessageDetail()
    {
        ShowMessageDetailModal = false;
        SelectedMessage = null;
    }

    public async Task<List<ServiceBusMessage>> LockMessagesForModalAsync(int count, int timeoutSeconds, bool fromDeadLetter)
    {
        string entityPath = "";
        if (State.SelectedQueueName != null)
        {
            entityPath = fromDeadLetter ? $"{State.SelectedQueueName}/$DeadLetterQueue" : State.SelectedQueueName;
        }
        else if (State.IsSubscriptionSelected)
        {
            var subPath = $"{State.SelectedTopicName}/subscriptions/{State.SelectedSubscriptionName}";
            entityPath = fromDeadLetter ? $"{subPath}/$DeadLetterQueue" : subPath;
        }

        var token = await GetTokenAsync(entityPath);
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Service Bus token not available");

        if (State.SelectedQueueName != null)
            return await _jsInterop.ReceiveAndLockQueueMessagesAsync(NamespaceNameOnly, State.SelectedQueueName, token, timeoutSeconds, State.IsViewingDLQ, count);
        else if (State.IsSubscriptionSelected)
            return await _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(NamespaceNameOnly, State.SelectedTopicName!, State.SelectedSubscriptionName!, token, timeoutSeconds, State.IsViewingDLQ, count);
        return new List<ServiceBusMessage>();
    }

    public async Task PeekLockMessagesWithSettings((int count, int duration) settings)
    {
        ShowPeekLockModal = false;
        IsPeeking = true;
        PeekLockCount = settings.count;
        PeekLockDuration = settings.duration;
        try { await PeekMessagesAsync(); }
        finally { IsPeeking = false; NotifyStateChanged(); }
    }

    public void ShowMessageDetail(ServiceBusMessage message)
    {
        SelectedMessage = message;
        ShowMessageDetailModal = true;
        NotifyStateChanged();
    }

    public void ShowScheduledMessageWarning(string message)
    {
        _notificationService.NotifyWarning(message);
    }

    public void ClearError()
    {
        ErrorMessage = null;
        RawErrorMessage = null;
        NotifyStateChanged();
    }

    public void DeleteSingleMessage(long sequenceNumber)
    {
        CloseMessageDetail();
        DeleteMessages(new List<long> { sequenceNumber });
    }

    public void ResubmitSingleMessage(long sequenceNumber)
    {
        CloseMessageDetail();
        ResubmitMessages(new List<long> { sequenceNumber });
    }

    public void MoveToDLQSingleMessage(long sequenceNumber)
    {
        CloseMessageDetail();
        MoveToDLQMessages(new List<long> { sequenceNumber });
    }

    public void DownloadSelectedMessagesAsync(List<long> sequenceNumbers)
    {
        _currentExportMode = "selected";
        _currentExportSequenceNumbers = sequenceNumbers;
        ShowExportOptionsModal = true;
        NotifyStateChanged();
    }

    public void DownloadLoadedMessagesAsync()
    {
        _currentExportMode = "loaded";
        _currentExportSequenceNumbers.Clear();
        ShowExportOptionsModal = true;
        NotifyStateChanged();
    }

    public void DownloadEntireEntityMessagesAsync()
    {
        _currentExportMode = "all";
        _currentExportSequenceNumbers.Clear();
        ShowExportOptionsModal = true;
        NotifyStateChanged();
    }

    public async Task ExecuteExportAsync()
    {
        ShowExportOptionsModal = false;
        IsProcessingBatch = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            List<ServiceBusMessage> messages = new();

            if (_currentExportMode == "selected")
            {
                messages = PeekedMessages.Where(m => m.SequenceNumber.HasValue && _currentExportSequenceNumbers.Contains(m.SequenceNumber.Value)).ToList();
            }
            else if (_currentExportMode == "loaded")
            {
                messages = PeekedMessages;
            }
            else if (_currentExportMode == "all")
            {
                bool isQueue = State.IsQueueSelected;
                string entityPath = isQueue ? State.SelectedQueueName! : State.SelectedTopicName!;
                string? subName = isQueue ? null : State.SelectedSubscriptionName;
                string tokenPath;
                if (isQueue)
                {
                    tokenPath = State.IsViewingDLQ ? $"{entityPath}/$DeadLetterQueue" : entityPath;
                }
                else
                {
                    var subPath = $"{entityPath}/subscriptions/{subName}";
                    tokenPath = State.IsViewingDLQ ? $"{subPath}/$DeadLetterQueue" : subPath;
                }
                var token = await GetTokenAsync(tokenPath);
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException("Failed to get authentication token.");
                }

                int countToFetch = 10000;
                int currentSequence = 0;

                while (countToFetch > 0)
                {
                    List<ServiceBusMessage> batch;
                    if (isQueue)
                    {
                        batch = await _jsInterop.PeekQueueMessagesAsync(NamespaceNameOnly, entityPath, token, Math.Min(250, countToFetch), currentSequence, State.IsViewingDLQ);
                    }
                    else
                    {
                        batch = await _jsInterop.PeekSubscriptionMessagesAsync(NamespaceNameOnly, entityPath, subName!, token, Math.Min(250, countToFetch), currentSequence, State.IsViewingDLQ);
                    }

                    if (batch == null || batch.Count == 0)
                    {
                        break;
                    }

                    messages.AddRange(batch);
                    countToFetch -= batch.Count;

                    var lastSeq = batch.Last().SequenceNumber;
                    if (!lastSeq.HasValue) break;

                    currentSequence = (int)lastSeq.Value + 1;
                }
            }

            if (messages.Any())
            {
                var json = FilterJsonProperties(messages, MessageExportOptions);
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var entityName = State.IsQueueSelected ? State.SelectedQueueName : State.SelectedSubscriptionName;
                var fileName = $"{entityName}_{_currentExportMode}_{timestamp}.json";

                await _jsRuntime.InvokeVoidAsync("downloadFile", fileName, "application/json", json);
                _notificationService.NotifySuccess($"Downloaded {messages.Count} messages successfully.");
            }
            else
            {
                _notificationService.NotifyError("No messages found to export.");
            }
        }
        catch (Exception ex)
        {
            RawErrorMessage = ex.Message;
            ErrorMessage = PermissionErrorHelper.FormatError(ex.Message, "export messages from");
        }
        finally
        {
            IsProcessingBatch = false;
            NotifyStateChanged();
        }
    }

    private string FilterJsonProperties(List<ServiceBusMessage> messages, Bussin.Components.ExportOptionsModal.ExportOptions options)
    {
        var node = JsonSerializer.SerializeToNode(messages);
        if (node is System.Text.Json.Nodes.JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is System.Text.Json.Nodes.JsonObject obj)
                {
                    if (!options.IncludeMessageId) obj.Remove("messageId");
                    if (!options.IncludeSessionId) obj.Remove("sessionId");
                    if (!options.IncludeCorrelationId) obj.Remove("correlationId");
                    if (!options.IncludeSubject) obj.Remove("subject");
                    if (!options.IncludeReplyTo) obj.Remove("replyTo");
                    if (!options.IncludeTo) obj.Remove("to");
                    if (!options.IncludeContentType) obj.Remove("contentType");
                    if (!options.IncludePartitionKey) obj.Remove("partitionKey");
                    if (!options.IncludeSequenceNumber) obj.Remove("sequenceNumber");
                    if (!options.IncludeEnqueuedTime) obj.Remove("enqueuedTime");
                    if (!options.IncludeDeliveryCount) obj.Remove("deliveryCount");
                    if (!options.IncludeTtl) obj.Remove("ttl");
                    if (!options.IncludeExpiryTime) obj.Remove("expiryTime");
                    if (!options.IncludeLockedUntil) obj.Remove("lockedUntil");
                    
                    if (!options.IncludeCustomProperties)
                    {
                        obj.Remove("applicationProperties");
                    }
                    
                    obj.Remove("messageAnnotations");
                    obj.Remove("properties");
                    obj.Remove("creationTime");
                    obj.Remove("lockToken");
                }
            }
            return jsonArray.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        return "[]";
    }

    private string GetEntityPath()
    {
        if (State.SelectedQueueName != null)
        {
            return State.IsViewingDLQ ? $"{State.SelectedQueueName}/$DeadLetterQueue" : State.SelectedQueueName;
        }
        if (State.SelectedTopicName != null && State.SelectedSubscriptionName != null)
        {
            var subPath = $"{State.SelectedTopicName}/subscriptions/{State.SelectedSubscriptionName}";
            return State.IsViewingDLQ ? $"{subPath}/$DeadLetterQueue" : subPath;
        }
        return "";
    }

    private async Task<string> GetTokenAsync(string entityPath)
    {
        var connection = _navState.GetNamespaceConnection(State.FullyQualifiedNamespace);
        if (connection != null && !string.IsNullOrEmpty(connection.ConnectionString))
        {
            var (endpoint, keyName, key, defaultEntityPath) = ServiceBusConnectionStringHelper.ParseConnectionString(connection.ConnectionString);
            var activePath = !string.IsNullOrEmpty(defaultEntityPath) ? defaultEntityPath : entityPath;
            return ServiceBusConnectionStringHelper.GenerateSasToken(connection.ConnectionString, activePath, TimeSpan.FromHours(2));
        }

        var token = await _authService.GetServiceBusTokenAsync();
        return token;
    }

    public void Dispose()
    {
        _confirmModal.OnChange -= NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged -= NotifyStateChanged;
        _backgroundPurge.OnPurgeCompleted -= HandlePurgeCompleted;
        _backgroundResubmit.OnOperationsChanged -= NotifyStateChanged;
        _backgroundSearch.OnViewResultsRequested -= OnViewSearchResultsRequested;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    private void HandlePurgeCompleted(PurgeOperation op)
    {
        if (State.CurrentNamespace == null || (op.NamespaceName != State.FullyQualifiedNamespace && op.NamespaceName != NamespaceNameOnly)) return;

        if (op.EntityType == "queue")
        {
            if (QueueDict.TryGetValue(op.EntityPath, out var queue))
            {
                if (op.IsDeadLetter)
                {
                    QueueDict[op.EntityPath] = queue with { DeadLetterMessageCount = 0 };
                }
                else
                {
                    QueueDict[op.EntityPath] = queue with { ActiveMessageCount = 0 };
                }
                NotifyStateChanged();
            }
        }
        else if (op.EntityType == "subscription")
        {
            var parts = op.EntityPath.Split('/');
            if (parts.Length >= 2)
            {
                string topicName = parts[0];
                string subscriptionName = parts[^1];

                if (State.SelectedTopicName == topicName && SubscriptionDict.TryGetValue(subscriptionName, out var sub))
                {
                    if (op.IsDeadLetter)
                    {
                        SubscriptionDict[subscriptionName] = sub with { DeadLetterMessageCount = 0 };
                    }
                    else
                    {
                        SubscriptionDict[subscriptionName] = sub with { ActiveMessageCount = 0 };
                    }
                    NotifyStateChanged();
                }
            }
        }

        // Clear peeked messages if the purged entity is the selected one and the DLQ view matches
        bool isCurrentSelected = false;
        if (op.EntityType == "queue" && State.SelectedQueueName == op.EntityPath)
        {
            isCurrentSelected = true;
        }
        else if (op.EntityType == "subscription")
        {
            var parts = op.EntityPath.Split('/');
            if (parts.Length >= 2)
            {
                string topicName = parts[0];
                string subscriptionName = parts[^1];
                if (State.SelectedTopicName == topicName && State.SelectedSubscriptionName == subscriptionName)
                {
                    isCurrentSelected = true;
                }
            }
        }

        if (isCurrentSelected && State.IsViewingDLQ == op.IsDeadLetter)
        {
            ClearPeekedMessages();
            NotifyStateChanged();
        }
    }
}
