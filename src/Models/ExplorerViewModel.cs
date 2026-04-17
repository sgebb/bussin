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
        NavigationStateService navState)
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

        // Initialize state listeners
        _confirmModal.OnChange += NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged += NotifyStateChanged;
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
    private string? _lastNamespace;
    private string? _loadedSubscriptionsForTopic;

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

    public async Task InitializeAsync(string? namespaceParam, string? resourceGroupParam, string? subscriptionIdParam, string? nameParam)
    {
        if (!string.IsNullOrEmpty(namespaceParam))
        {
            var connection = _navState.GetNamespaceConnection(namespaceParam);
            
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

            // Only load if we have the minimum required information for the ResourceIdentifier
            if (!string.IsNullOrEmpty(State.CurrentNamespace?.SubscriptionId) && 
                !string.IsNullOrEmpty(State.CurrentNamespace?.ResourceGroup))
            {
                await LoadEntitiesAsync();
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

            await Task.WhenAll(
                LoadQueuesAsync(credential, State.CurrentNamespace),
                LoadTopicsAsync(credential, State.CurrentNamespace)
            );
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

    private async Task LoadQueuesAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo)
    {
        var ct = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            await foreach (var queue in _resourceService.ListQueuesAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                QueueDict[queue.Name] = queue;
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadTopicsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo)
    {
        var ct = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            await foreach (var topic in _resourceService.ListTopicsAsync(credential, namespaceInfo, ct))
            {
                if (ct.IsCancellationRequested) break;
                TopicDict[topic.Name] = topic;
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshEntitiesAsync()
    {
        QueueDict.Clear();
        TopicDict.Clear();
        SubscriptionDict.Clear();
        _loadedSubscriptionsForTopic = null;

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

            var firstItem = true;
            await foreach (var sub in _resourceService.ListSubscriptionsAsync(credential, State.CurrentNamespace, topicName, ct))
            {
                if (ct.IsCancellationRequested) break;

                if (State.SelectedTopicName == topicName)
                {
                    SubscriptionDict[sub.Name] = sub;
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
            var token = await _authService.GetServiceBusTokenAsync();
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
            var token = await _authService.GetServiceBusTokenAsync();
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
            await _backgroundPurge.StartPurgeAsync(NamespaceNameOnly, entityType, entityPath, State.IsViewingDLQ);
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
            await _backgroundResubmit.StartResubmitAsync(NamespaceNameOnly, entityType, entityPath, ResubmitRemoveFromDLQ);
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
                var token = await _authService.GetServiceBusTokenAsync();
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
            NamespaceName = NamespaceNameOnly,
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
        var token = await _authService.GetServiceBusTokenAsync();
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

    public void Dispose()
    {
        _confirmModal.OnChange -= NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged -= NotifyStateChanged;
        _backgroundResubmit.OnOperationsChanged -= NotifyStateChanged;
        _backgroundSearch.OnViewResultsRequested -= OnViewSearchResultsRequested;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
