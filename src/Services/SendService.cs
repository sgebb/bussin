using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bussin.Models;
using Bussin.Components;

namespace Bussin.Services;

/// <summary>
/// Scoped service coordinating message sending, batch operations, 
/// and resubmission of edited messages.
/// </summary>
public sealed class SendService
{
    private readonly MessageListState _messageListState;
    private readonly EntitySelectionState _entitySelectionState;
    private readonly IServiceBusOperationsService _operationsService;
    private readonly INotificationService _notificationService;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public bool IsSending { get; private set; }
    public bool IsProcessingBatch { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? RawErrorMessage { get; private set; }

    public SendService(
        MessageListState messageListState,
        EntitySelectionState entitySelectionState,
        IServiceBusOperationsService operationsService,
        INotificationService notificationService)
    {
        _messageListState = messageListState;
        _entitySelectionState = entitySelectionState;
        _operationsService = operationsService;
        _notificationService = notificationService;
    }

    public void ClearError()
    {
        ErrorMessage = null;
        RawErrorMessage = null;
        NotifyStateChanged();
    }

    public async Task SendMessageAsync(SendMessageModal.SendMessageRequest request)
    {
        IsSending = true;
        ErrorMessage = null;
        NotifyStateChanged();
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

            var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
            var state = _entitySelectionState.State;

            if (state.SelectedQueueName != null)
                await _operationsService.SendQueueMessageAsync(namespaceOnly, state.SelectedQueueName, request.Body, props);
            else if (state.SelectedTopicName != null)
                await _operationsService.SendTopicMessageAsync(namespaceOnly, state.SelectedTopicName, request.Body, props);

            _notificationService.NotifySuccess("Message sent successfully");
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
        NotifyStateChanged();
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

            var state = _entitySelectionState.State;
            var namespaceOnly = _entitySelectionState.NamespaceNameOnly;

            // 1. Send the edited message back to the active queue/topic
            if (state.IsQueueSelected)
                await _operationsService.SendQueueMessageAsync(namespaceOnly, state.SelectedQueueName!, request.Body, props);
            else if (state.IsSubscriptionSelected)
                await _operationsService.SendTopicMessageAsync(namespaceOnly, state.SelectedTopicName!, request.Body, props);

            // 2. If it's a DLQ message and the user checked "delete original", delete it
            if (state.IsViewingDLQ && request.DeleteOriginal)
            {
                if (state.IsQueueSelected)
                {
                    await _operationsService.DeleteQueueMessagesAsync(namespaceOnly, state.SelectedQueueName!, new[] { request.OriginalSequenceNumber }, fromDeadLetter: true);
                }
                else if (state.IsSubscriptionSelected)
                {
                    await _operationsService.DeleteSubscriptionMessagesAsync(namespaceOnly, state.SelectedTopicName!, state.SelectedSubscriptionName!, new[] { request.OriginalSequenceNumber }, fromDeadLetter: true);
                }

                // Remove it from the local list
                _messageListState.PeekedMessages.RemoveAll(m => m.SequenceNumber == request.OriginalSequenceNumber);
                _messageListState.NotifyUpdate();
            }

            _notificationService.NotifySuccess("Message resubmitted successfully");
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
        NotifyStateChanged();
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

            var state = _entitySelectionState.State;
            var namespaceOnly = _entitySelectionState.NamespaceNameOnly;

            if (state.SelectedQueueName != null)
                await _operationsService.SendQueueMessagesBatchAsync(namespaceOnly, state.SelectedQueueName, batchObjects);
            else if (state.SelectedTopicName != null)
                await _operationsService.SendTopicMessagesBatchAsync(namespaceOnly, state.SelectedTopicName, batchObjects);

            _notificationService.NotifySuccess("Batch sent successfully");
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
}
