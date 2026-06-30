using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Scoped service coordinating message retrieval, receive-and-lock operations, 
/// settlement (delete, resubmit, move to DLQ), background bulk jobs, and export.
/// </summary>
public sealed class PeekService : IDisposable
{
    private readonly MessageListState _messageListState;
    private readonly EntitySelectionState _entitySelectionState;
    private readonly IServiceBusJsInteropService _jsInterop;
    private readonly INotificationService _notificationService;
    private readonly IAuthenticationService _authService;
    private readonly NavigationStateService _navState;
    private readonly IJSRuntime _jsRuntime;
    private readonly IConfirmModalService _confirmModal;
    private readonly BackgroundPurgeService _backgroundPurge;
    private readonly BackgroundResubmitService _backgroundResubmit;
    private readonly IServiceBusOperationsService _operationsService;
    private readonly ExplorerDialogService _dialogService;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public bool IsPeeking { get; private set; }
    public bool IsProcessingBatch { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? RawErrorMessage { get; private set; }
    
    public int PeekLockCount { get; set; } = 10;
    public int PeekLockDuration { get; set; } = 30;

    public PurgeOperation? CurrentPurgeOperation
    {
        get
        {
            var currentEntity = _entitySelectionState.State.GetEntityPath();
            var currentDLQ = _entitySelectionState.State.IsViewingDLQ;
            return _backgroundPurge.ActiveOperations.FirstOrDefault(op =>
                op.EntityPath == currentEntity && op.IsDeadLetter == currentDLQ);
        }
    }

    public ResubmitOperation? CurrentResubmitOperation
    {
        get
        {
            var currentEntity = _entitySelectionState.State.GetEntityPath();
            return _backgroundResubmit.ActiveOperations.FirstOrDefault(op =>
                op.EntityPath == currentEntity);
        }
    }

    public PeekService(
        MessageListState messageListState,
        EntitySelectionState entitySelectionState,
        IServiceBusJsInteropService jsInterop,
        INotificationService notificationService,
        IAuthenticationService authService,
        NavigationStateService navState,
        IJSRuntime jsRuntime,
        IConfirmModalService confirmModal,
        BackgroundPurgeService backgroundPurge,
        BackgroundResubmitService backgroundResubmit,
        IServiceBusOperationsService operationsService,
        ExplorerDialogService dialogService)
    {
        _messageListState = messageListState;
        _entitySelectionState = entitySelectionState;
        _jsInterop = jsInterop;
        _notificationService = notificationService;
        _authService = authService;
        _navState = navState;
        _jsRuntime = jsRuntime;
        _confirmModal = confirmModal;
        _backgroundPurge = backgroundPurge;
        _backgroundResubmit = backgroundResubmit;
        _operationsService = operationsService;
        _dialogService = dialogService;

        _confirmModal.OnChange += NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged += NotifyStateChanged;
        _backgroundPurge.OnPurgeCompleted += HandlePurgeCompleted;
        _backgroundResubmit.OnOperationsChanged += NotifyStateChanged;
    }

    public void ClearError()
    {
        ErrorMessage = null;
        RawErrorMessage = null;
        NotifyStateChanged();
    }

    public async Task<string> GetTokenAsync(string entityPath)
    {
        var connection = _navState.GetNamespaceConnection(_entitySelectionState.State.FullyQualifiedNamespace);
        if (connection != null && !string.IsNullOrEmpty(connection.ConnectionString))
        {
            var (endpoint, keyName, key, defaultEntityPath) = ServiceBusConnectionStringHelper.ParseConnectionString(connection.ConnectionString);
            var activePath = !string.IsNullOrEmpty(defaultEntityPath) ? defaultEntityPath : entityPath;
            return ServiceBusConnectionStringHelper.GenerateSasToken(connection.ConnectionString, activePath, TimeSpan.FromHours(2));
        }

        var token = await _authService.GetServiceBusTokenAsync();
        return token;
    }

    private string GetEntityPath()
    {
        if (_entitySelectionState.State.SelectedQueueName != null)
        {
            return _entitySelectionState.State.IsViewingDLQ ? $"{_entitySelectionState.State.SelectedQueueName}/$DeadLetterQueue" : _entitySelectionState.State.SelectedQueueName;
        }
        if (_entitySelectionState.State.SelectedTopicName != null && _entitySelectionState.State.SelectedSubscriptionName != null)
        {
            var subPath = $"{_entitySelectionState.State.SelectedTopicName}/subscriptions/{_entitySelectionState.State.SelectedSubscriptionName}";
            return _entitySelectionState.State.IsViewingDLQ ? $"{subPath}/$DeadLetterQueue" : subPath;
        }
        return "";
    }

    public async Task PeekMessagesAsync(bool append = false)
    {
        if (append && _messageListState.LastPeekOptions != null)
        {
            await PeekWithOptionsAsync(_messageListState.LastPeekOptions, append: true);
            return;
        }

        if (append && _messageListState.PeekedMessages.Count >= 1000)
        {
            ErrorMessage = "Maximum of 1000 messages loaded. Use filters or start from a different sequence number.";
            NotifyStateChanged();
            return;
        }

        IsPeeking = true;
        ErrorMessage = null;
        NotifyStateChanged();

        if (!append)
        {
            _messageListState.HasPeeked = true;
            _messageListState.PeekedMessages.Clear();
            _messageListState.PeekFromSequence = 0;
            _messageListState.LastPeekOptions = null;
            _messageListState.SelectedMessage = null;
            _messageListState.NotifyUpdate();
        }
        else if (_messageListState.PeekedMessages.Any())
        {
            _messageListState.PeekFromSequence = (int)(_messageListState.PeekedMessages.Max(m => m.SequenceNumber ?? 0) + 1);
        }

        try
        {
            var token = await GetTokenAsync(GetEntityPath());
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Service Bus token not available";
                NotifyStateChanged();
                return;
            }

            var messages = await PeekEntityMessagesInternalAsync(token);

            if (append)
            {
                _messageListState.PeekedMessages.AddRange(messages);
            }
            else
            {
                _messageListState.PeekedMessages = messages;
            }

            _messageListState.NotifyUpdate();

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
        var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
        var state = _entitySelectionState.State;
        
        if (state.SelectedQueueName != null)
        {
            return await _jsInterop.PeekQueueMessagesAsync(namespaceOnly, state.SelectedQueueName, token, _messageListState.PeekCount, _messageListState.PeekFromSequence, state.IsViewingDLQ);
        }
        if (state.SelectedTopicName != null && state.SelectedSubscriptionName != null)
        {
            return await _jsInterop.PeekSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName, state.SelectedSubscriptionName, token, _messageListState.PeekCount, _messageListState.PeekFromSequence, state.IsViewingDLQ);
        }
        return new List<ServiceBusMessage>();
    }

    public async Task PeekWithOptionsAsync(PeekOptions options, bool append = false)
    {
        IsPeeking = true;
        ErrorMessage = null;
        NotifyStateChanged();

        if (!append)
        {
            _messageListState.HasPeeked = true;
            _messageListState.PeekedMessages.Clear();
            _messageListState.LastPeekOptions = options;
            _messageListState.SelectedMessage = null;
            _messageListState.NotifyUpdate();
        }

        try
        {
            var token = await GetTokenAsync(GetEntityPath());
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Service Bus token not available";
                NotifyStateChanged();
                return;
            }

            long startSequence = options.FromSequenceNumber;
            if (append)
            {
                if (options.PeekFromNewest && _messageListState.PeekedMessages.Any())
                {
                    var oldestSeq = _messageListState.PeekedMessages.Min(m => m.SequenceNumber ?? 0);
                    startSequence = Math.Max(0, oldestSeq - options.MaxCount);
                }
                else
                {
                    startSequence = _messageListState.PeekFromSequence;
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
                _messageListState.PeekFromSequence = (int)result.nextSequence;
            }
            else
            {
                messages = await PeekIterativeAsync(token, options.MaxCount, startSequence, options.SessionId);
                if (messages.Any())
                {
                    _messageListState.PeekFromSequence = (int)(messages.Max(m => m.SequenceNumber ?? 0) + 1);
                }
            }

            if (options.PeekFromNewest)
            {
                messages = messages.OrderByDescending(m => m.SequenceNumber ?? 0).ToList();
            }

            if (append)
            {
                var existingSequences = _messageListState.PeekedMessages.Where(m => m.SequenceNumber.HasValue).Select(m => m.SequenceNumber!.Value).ToHashSet();
                var newMessages = messages.Where(m => !m.SequenceNumber.HasValue || !existingSequences.Contains(m.SequenceNumber.Value)).ToList();
                _messageListState.PeekedMessages.AddRange(newMessages);
                messages = newMessages;
            }
            else
            {
                _messageListState.PeekedMessages = messages;
            }

            _messageListState.NotifyUpdate();

            if (messages.Count > 0)
            {
                var filterMsg = options.HasActiveFilter ? $" matching filters" : "";
                var suffix = append ? " more" : "";
                var entityType = _entitySelectionState.State.IsQueueSelected ? "queue" : "subscription";
                var entityName = _entitySelectionState.State.GetEntityPath();
                var source = _entitySelectionState.State.IsViewingDLQ ? $"DLQ of {entityType} '{entityName}'" : $"{entityType} '{entityName}'";
                _notificationService.NotifySuccess($"Peeked {messages.Count}{suffix} messages from {source}{filterMsg}", "peek-result");
            }
            else
            {
                var entityType = _entitySelectionState.State.IsQueueSelected ? "queue" : "subscription";
                var entityName = _entitySelectionState.State.GetEntityPath();
                var source = _entitySelectionState.State.IsViewingDLQ ? $"DLQ of {entityType} '{entityName}'" : $"{entityType} '{entityName}'";
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

    private Task<List<ServiceBusMessage>> PeekBatchAsync(string token, int count, long startSequence, string? sessionId = null)
    {
        var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
        var state = _entitySelectionState.State;
        if (state.SelectedQueueName != null)
            return _jsInterop.PeekQueueMessagesAsync(namespaceOnly, state.SelectedQueueName, token, count, (int)startSequence, state.IsViewingDLQ, sessionId);
        if (state.IsSubscriptionSelected)
            return _jsInterop.PeekSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, token, count, (int)startSequence, state.IsViewingDLQ, sessionId);
        return Task.FromResult(new List<ServiceBusMessage>());
    }

    private async Task<List<ServiceBusMessage>> PeekIterativeAsync(string token, int desiredCount, long startSequence, string? sessionId = null)
    {
        const int batchSize = 250;
        var allMessages = new List<ServiceBusMessage>();
        var currentSequence = startSequence;

        while (allMessages.Count < desiredCount)
        {
            var fetchCount = Math.Min(desiredCount - allMessages.Count, batchSize);
            var batch = await PeekBatchAsync(token, fetchCount, currentSequence, sessionId);
            if (batch.Count == 0) break;
            allMessages.AddRange(batch);
            currentSequence = batch.Max(m => m.SequenceNumber ?? 0) + 1;
            if (batch.Count < fetchCount) break;
        }
        return allMessages;
    }

    private async Task<long> CalculateNewestStartSequenceAsync(string token, int maxCount)
    {
        try
        {
            var firstMessage = await PeekBatchAsync(token, 1, 0);
            if (!firstMessage.Any()) return 0;
            var firstSeq = firstMessage[0].SequenceNumber ?? 0;
            long totalMessages = _entitySelectionState.CurrentEntityMessageCount;
            if (totalMessages <= maxCount) return 0;
            return Math.Max(0, firstSeq + (totalMessages - maxCount));
        }
        catch { return 0; }
    }

    private async Task<(List<ServiceBusMessage> messages, long nextSequence)> PeekWithFiltersAsync(string token, PeekOptions options, long startSequence)
    {
        var matchingMessages = new List<ServiceBusMessage>();
        var currentSequence = startSequence;
        const int batchSize = 100;
        const int maxAttempts = 100;
        var attempts = 0;

        while (matchingMessages.Count < 50 && attempts < maxAttempts)
        {
            var batch = await PeekBatchAsync(token, batchSize, currentSequence, options.SessionId);
            if (!batch.Any()) break;
            matchingMessages.AddRange(batch.Where(m =>
            {
                if (!string.IsNullOrWhiteSpace(options.BodyFilter) && m.Body?.Contains(options.BodyFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                if (!string.IsNullOrWhiteSpace(options.MessageIdFilter) && m.MessageId?.Contains(options.MessageIdFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                if (!string.IsNullOrWhiteSpace(options.SubjectFilter) && m.Subject?.Contains(options.SubjectFilter, StringComparison.OrdinalIgnoreCase) != true) return false;
                return true;
            }));
            currentSequence = batch.Max(m => m.SequenceNumber ?? 0) + 1;
            attempts++;
        }
        return (matchingMessages.Take(50).ToList(), currentSequence);
    }

    public async Task<List<ServiceBusMessage>> LockMessagesForModalAsync(int count, int durationSeconds, bool fromDeadLetter, string? sessionId = null)
    {
        string entityPath = "";
        var state = _entitySelectionState.State;
        
        if (state.SelectedQueueName != null)
        {
            entityPath = fromDeadLetter ? $"{state.SelectedQueueName}/$DeadLetterQueue" : state.SelectedQueueName;
        }
        else if (state.IsSubscriptionSelected)
        {
            var subPath = $"{state.SelectedTopicName}/subscriptions/{state.SelectedSubscriptionName}";
            entityPath = fromDeadLetter ? $"{subPath}/$DeadLetterQueue" : subPath;
        }

        var token = await GetTokenAsync(entityPath);
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Service Bus token not available");

        if (state.SelectedQueueName != null)
            return await _jsInterop.ReceiveAndLockQueueMessagesAsync(_entitySelectionState.NamespaceNameOnly, state.SelectedQueueName, token, durationSeconds, state.IsViewingDLQ, count, sessionId);
        else if (state.IsSubscriptionSelected)
            return await _jsInterop.ReceiveAndLockSubscriptionMessagesAsync(_entitySelectionState.NamespaceNameOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, token, durationSeconds, state.IsViewingDLQ, count, sessionId);
        
        return new List<ServiceBusMessage>();
    }

    public async Task PeekLockMessagesWithSettings((int count, int duration) settings)
    {
        _dialogService.ShowPeekLockModal = false;
        IsPeeking = true;
        PeekLockCount = settings.count;
        PeekLockDuration = settings.duration;
        NotifyStateChanged();
        try
        {
            await PeekMessagesAsync();
        }
        finally
        {
            IsPeeking = false;
            NotifyStateChanged();
        }
    }

    public void PurgeMessages()
    {
        var entityName = _entitySelectionState.State.GetEntityPath();
        var dlqSuffix = _entitySelectionState.State.IsViewingDLQ ? " (Dead Letter Queue)" : "";
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
        NotifyStateChanged();
        try
        {
            var entityType = _entitySelectionState.State.IsQueueSelected ? "queue" : "subscription";
            var entityPath = _entitySelectionState.State.GetEntityPath();
            await _backgroundPurge.StartPurgeAsync(_entitySelectionState.State.FullyQualifiedNamespace, entityType, entityPath, _entitySelectionState.State.IsViewingDLQ, _entitySelectionState.SelectedEntityRequiresSession);
            _messageListState.PeekedMessages.Clear();
            _messageListState.PeekFromSequence = 0;
            _messageListState.NotifyUpdate();
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
        if (!_entitySelectionState.State.IsViewingDLQ) return;
        _dialogService.ResubmitRemoveFromDLQ = true;
        _dialogService.IsResubmitModal = true;
        var entityName = _entitySelectionState.State.GetEntityPath();
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
        NotifyStateChanged();
        try
        {
            var entityType = _entitySelectionState.State.IsQueueSelected ? "queue" : "subscription";
            var entityPath = _entitySelectionState.State.GetEntityPath();
            await _backgroundResubmit.StartResubmitAsync(_entitySelectionState.State.FullyQualifiedNamespace, entityType, entityPath, _dialogService.ResubmitRemoveFromDLQ);
            _messageListState.PeekedMessages.Clear();
            _messageListState.PeekFromSequence = 0;
            _messageListState.NotifyUpdate();
            _confirmModal.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start resubmit: {ex.Message}";
        }
        finally
        {
            _confirmModal.IsProcessing = false;
            _dialogService.IsResubmitModal = false;
            NotifyStateChanged();
        }
    }

    public void DeleteSingleMessage(long sequenceNumber)
    {
        _dialogService.CloseMessageDetail();
        DeleteMessages(new List<long> { sequenceNumber });
    }

    public void ResubmitSingleMessage(long sequenceNumber)
    {
        _dialogService.CloseMessageDetail();
        ResubmitMessages(new List<long> { sequenceNumber });
    }

    public void MoveToDLQSingleMessage(long sequenceNumber)
    {
        _dialogService.CloseMessageDetail();
        MoveToDLQMessages(new List<long> { sequenceNumber });
    }


    private string? GetSessionIdForSequences(List<long> sequenceNumbers)
    {
        if (!_entitySelectionState.SelectedEntityRequiresSession || _entitySelectionState.State.IsViewingDLQ)
        {
            return null;
        }
        var firstSeq = sequenceNumbers.FirstOrDefault();
        var msg = _messageListState.PeekedMessages.FirstOrDefault(m => m.SequenceNumber == firstSeq);
        return msg?.SessionId;
    }

    public void DeleteMessages(List<long> sequenceNumbers)
    {
        var scheduledSeqNums = new List<long>();
        var regularSeqNums = new List<long>();
        
        foreach (var seq in sequenceNumbers)
        {
            var msg = _messageListState.PeekedMessages.FirstOrDefault(m => m.SequenceNumber == seq);
            if (msg != null && MessageHelpers.IsScheduledMessage(msg))
            {
                scheduledSeqNums.Add(seq);
            }
            else
            {
                regularSeqNums.Add(seq);
            }
        }

        string title = "Confirm Delete";
        string message = $"Are you sure you want to delete {sequenceNumbers.Count} message(s)?";
        string confirmText = "Delete Messages";
        
        if (scheduledSeqNums.Any() && !regularSeqNums.Any())
        {
            title = "Confirm Cancel Scheduled";
            message = $"Are you sure you want to cancel {scheduledSeqNums.Count} scheduled message(s)?";
            confirmText = "Cancel Messages";
        }
        else if (scheduledSeqNums.Any() && regularSeqNums.Any())
        {
            title = "Confirm Delete & Cancel";
            message = $"Are you sure you want to delete {regularSeqNums.Count} message(s) and cancel {scheduledSeqNums.Count} scheduled message(s)?";
            confirmText = "Confirm";
        }

        _confirmModal.Show(
            title: title,
            message: message,
            detail: "This action cannot be undone.",
            confirmText: confirmText,
            confirmClass: "btn-danger",
            onConfirm: () => ExecuteBatchDeleteAndCancelAsync(regularSeqNums, scheduledSeqNums)
        );
        _dialogService.IsResubmitModal = false;
    }

    private async Task ExecuteBatchDeleteAndCancelAsync(List<long> regularSeqNums, List<long> scheduledSeqNums)
    {
        _confirmModal.IsProcessing = true;
        ErrorMessage = null;
        NotifyStateChanged();
        try
        {
            var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
            var state = _entitySelectionState.State;

            if (regularSeqNums.Any())
            {
                var sessionId = GetSessionIdForSequences(regularSeqNums);
                if (state.IsQueueSelected)
                    await _operationsService.DeleteQueueMessagesAsync(namespaceOnly, state.SelectedQueueName!, regularSeqNums.ToArray(), state.IsViewingDLQ, sessionId);
                else if (state.IsSubscriptionSelected)
                    await _operationsService.DeleteSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, regularSeqNums.ToArray(), state.IsViewingDLQ, sessionId);
                
                _messageListState.PeekedMessages.RemoveAll(m => m.SequenceNumber.HasValue && regularSeqNums.Contains(m.SequenceNumber.Value));
            }

            if (scheduledSeqNums.Any())
            {
                if (state.IsQueueSelected)
                    await _operationsService.CancelScheduledQueueMessagesAsync(namespaceOnly, state.SelectedQueueName!, scheduledSeqNums.ToArray());
                else if (state.IsSubscriptionSelected)
                    await _operationsService.CancelScheduledTopicMessagesAsync(namespaceOnly, state.SelectedTopicName!, scheduledSeqNums.ToArray());
                
                _messageListState.PeekedMessages.RemoveAll(m => m.SequenceNumber.HasValue && scheduledSeqNums.Contains(m.SequenceNumber.Value));
            }

            if (_messageListState.PeekedMessages.Count == 0) _messageListState.PeekFromSequence = 0;
            
            _messageListState.NotifyUpdate();
            _confirmModal.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete/cancel messages: {ex.Message}";
        }
        finally
        {
            _confirmModal.IsProcessing = false;
            _dialogService.IsMoveToDLQModal = false;
            NotifyStateChanged();
        }
    }

    public void ResubmitMessages(List<long> sequenceNumbers)
    {
        _dialogService.ResubmitRemoveFromDLQ = true;
        _dialogService.IsResubmitModal = true;
        _confirmModal.Show(
            title: "Confirm Resubmit",
            message: $"Resubmit {sequenceNumbers.Count} message(s) from Dead Letter Queue?",
            detail: "Messages will be sent back to the main queue/subscription.",
            confirmText: "Resubmit",
            confirmClass: "btn-primary",
            onConfirm: async () =>
            {
                var shouldRemove = _dialogService.ResubmitRemoveFromDLQ;
                var sessionId = GetSessionIdForSequences(sequenceNumbers);
                await ExecuteBatchOperationAsync(sequenceNumbers, "resubmit", async (seqNums) =>
                {
                    var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
                    var state = _entitySelectionState.State;

                    if (state.IsQueueSelected)
                        await _operationsService.ResendQueueMessagesAsync(namespaceOnly, state.SelectedQueueName!, seqNums.ToArray(), state.IsViewingDLQ, shouldRemove, sessionId);
                    else if (state.IsSubscriptionSelected)
                        await _operationsService.ResendSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, seqNums.ToArray(), state.IsViewingDLQ, shouldRemove, sessionId);
                }, removeFromView: shouldRemove);
            }
        );
    }

    public void MoveToDLQMessages(List<long> sequenceNumbers)
    {
        _dialogService.IsMoveToDLQModal = true;
        _dialogService.MoveToDLQReason = "Manual move to DLQ";
        _dialogService.MoveToDLQErrorDescription = "Moved by user";

        _confirmModal.Show(
            title: "Confirm Move to Dead Letter Queue",
            message: $"Move {sequenceNumbers.Count} message(s) to Dead Letter Queue?",
            detail: "Messages will be moved to the Dead Letter Queue.",
            confirmText: "Move to Dead Letter Queue",
            confirmClass: "btn-warning",
            onConfirm: () => ExecuteBatchOperationAsync(sequenceNumbers, "move to Dead Letter Queue", async (seqNums) =>
            {
                var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
                var state = _entitySelectionState.State;
                var reason = _dialogService.MoveToDLQReason;
                var desc = _dialogService.MoveToDLQErrorDescription;
                var sessionId = GetSessionIdForSequences(seqNums);

                if (state.IsQueueSelected)
                    await _operationsService.MoveToDLQQueueMessagesAsync(namespaceOnly, state.SelectedQueueName!, seqNums.ToArray(), reason, desc, sessionId);
                else if (state.IsSubscriptionSelected)
                    await _operationsService.MoveToDLQSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, seqNums.ToArray(), reason, desc, sessionId);
            })
        );
        _dialogService.IsResubmitModal = false;
    }


    private async Task ExecuteBatchOperationAsync(List<long> sequenceNumbers, string operationName, Func<List<long>, Task> operation, bool removeFromView = true)
    {
        _confirmModal.IsProcessing = true;
        ErrorMessage = null;
        NotifyStateChanged();
        try
        {
            await operation(sequenceNumbers);
            if (removeFromView)
            {
                _messageListState.PeekedMessages.RemoveAll(m => m.SequenceNumber.HasValue && sequenceNumbers.Contains(m.SequenceNumber.Value));
                if (_messageListState.PeekedMessages.Count == 0) _messageListState.PeekFromSequence = 0;
            }
            _messageListState.NotifyUpdate();
            _notificationService.NotifySuccess($"{char.ToUpper(operationName[0])}{operationName[1..]} {sequenceNumbers.Count} messages successfully.");
            _confirmModal.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to {operationName} messages: {ex.Message}";
        }
        finally
        {
            _confirmModal.IsProcessing = false;
            _dialogService.IsMoveToDLQModal = false;
            NotifyStateChanged();
        }
    }

    private void HandlePurgeCompleted(PurgeOperation op)
    {
        var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
        if (_entitySelectionState.State.CurrentNamespace == null || 
            (op.NamespaceName != _entitySelectionState.State.FullyQualifiedNamespace && op.NamespaceName != namespaceOnly))
        {
            return;
        }

        if (op.EntityType == "queue")
        {
            if (_entitySelectionState.QueueDict.TryGetValue(op.EntityPath, out var queue))
            {
                if (op.IsDeadLetter)
                {
                    _entitySelectionState.QueueDict[op.EntityPath] = queue with { DeadLetterMessageCount = 0 };
                }
                else
                {
                    _entitySelectionState.QueueDict[op.EntityPath] = queue with { ActiveMessageCount = 0 };
                }
                _entitySelectionState.RefreshEntitiesAsync().ConfigureAwait(false);
            }
        }
        else if (op.EntityType == "subscription")
        {
            var parts = op.EntityPath.Split('/');
            if (parts.Length >= 2)
            {
                var subscriptionName = parts.Last();
                if (_entitySelectionState.SubscriptionDict.TryGetValue(subscriptionName, out var sub))
                {
                    if (op.IsDeadLetter)
                        _entitySelectionState.SubscriptionDict[subscriptionName] = sub with { DeadLetterMessageCount = 0 };
                    else
                        _entitySelectionState.SubscriptionDict[subscriptionName] = sub with { ActiveMessageCount = 0 };
                    
                    _entitySelectionState.RefreshEntitiesAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private string _currentExportMode = "selected";
    private List<long> _currentExportSequenceNumbers = new();

    public void DownloadSelectedMessagesAsync(List<long> sequenceNumbers) => PrepareExport("selected", sequenceNumbers);
    public void DownloadLoadedMessagesAsync() => PrepareExport("loaded");
    public void DownloadEntireEntityMessagesAsync() => PrepareExport("all");

    private void PrepareExport(string mode, List<long>? sequenceNumbers = null)
    {
        _currentExportMode = mode;
        _currentExportSequenceNumbers = sequenceNumbers ?? new();
        _dialogService.ShowExportOptionsModal = true;
        NotifyStateChanged();
    }

    public async Task ExecuteExportAsync()
    {
        _dialogService.ShowExportOptionsModal = false;
        IsProcessingBatch = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            List<ServiceBusMessage> messages = new();

            if (_currentExportMode == "selected")
            {
                messages = _messageListState.PeekedMessages.Where(m => m.SequenceNumber.HasValue && _currentExportSequenceNumbers.Contains(m.SequenceNumber.Value)).ToList();
            }
            else if (_currentExportMode == "loaded")
            {
                messages = _messageListState.PeekedMessages;
            }
            else if (_currentExportMode == "all")
            {
                var state = _entitySelectionState.State;
                bool isQueue = state.IsQueueSelected;
                string entityPath = isQueue ? state.SelectedQueueName! : state.SelectedTopicName!;
                string? subName = isQueue ? null : state.SelectedSubscriptionName;
                string tokenPath;
                if (isQueue)
                {
                    tokenPath = state.IsViewingDLQ ? $"{entityPath}/$DeadLetterQueue" : entityPath;
                }
                else
                {
                    var subPath = $"{entityPath}/subscriptions/{subName}";
                    tokenPath = state.IsViewingDLQ ? $"{subPath}/$DeadLetterQueue" : subPath;
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
                        batch = await _jsInterop.PeekQueueMessagesAsync(_entitySelectionState.NamespaceNameOnly, entityPath, token, Math.Min(250, countToFetch), currentSequence, state.IsViewingDLQ);
                    }
                    else
                    {
                        batch = await _jsInterop.PeekSubscriptionMessagesAsync(_entitySelectionState.NamespaceNameOnly, entityPath, subName!, token, Math.Min(250, countToFetch), currentSequence, state.IsViewingDLQ);
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
                var json = FilterJsonProperties(messages, _dialogService.MessageExportOptions);
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var state = _entitySelectionState.State;
                var entityName = state.IsQueueSelected ? state.SelectedQueueName : state.SelectedSubscriptionName;
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

    public void Dispose()
    {
        _confirmModal.OnChange -= NotifyStateChanged;
        _backgroundPurge.OnOperationsChanged -= NotifyStateChanged;
        _backgroundPurge.OnPurgeCompleted -= HandlePurgeCompleted;
        _backgroundResubmit.OnOperationsChanged -= NotifyStateChanged;
    }
}
