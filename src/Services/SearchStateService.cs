using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Scoped service coordinating background search operations and handling 
/// rendering of matching search results.
/// </summary>
public sealed class SearchStateService : IDisposable
{
    private readonly BackgroundSearchService _backgroundSearch;
    private readonly MessageListState _messageListState;
    private readonly EntitySelectionState _entitySelectionState;
    private readonly PeekService _peekService;
    private readonly IServiceBusJsInteropService _jsInterop;
    private readonly INotificationService _notificationService;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public SearchStateService(
        BackgroundSearchService backgroundSearch,
        MessageListState messageListState,
        EntitySelectionState entitySelectionState,
        PeekService peekService,
        IServiceBusJsInteropService jsInterop,
        INotificationService notificationService)
    {
        _backgroundSearch = backgroundSearch;
        _messageListState = messageListState;
        _entitySelectionState = entitySelectionState;
        _peekService = peekService;
        _jsInterop = jsInterop;
        _notificationService = notificationService;

        _backgroundSearch.OnViewResultsRequested += OnViewSearchResultsRequested;
    }

    public async Task StartBackgroundSearchAsync(PeekOptions options)
    {
        var state = _entitySelectionState.State;
        
        var searchOptions = new BackgroundSearchOptions
        {
            NamespaceName = state.FullyQualifiedNamespace,
            EntityType = state.IsQueueSelected ? "queue" : "subscription",
            EntityPath = state.IsQueueSelected ? state.SelectedQueueName : $"{state.SelectedTopicName}/Subscriptions/{state.SelectedSubscriptionName}",
            TopicName = state.SelectedTopicName,
            SubscriptionName = state.SelectedSubscriptionName,
            IsDeadLetter = state.IsViewingDLQ,
            TotalMessageCount = _entitySelectionState.CurrentEntityMessageCount,
            BodyFilter = options.BodyFilter,
            MessageIdFilter = options.MessageIdFilter,
            SubjectFilter = options.SubjectFilter,
            MaxMatches = options.MaxCount
        };

        await _backgroundSearch.StartSearchAsync(searchOptions);
        _notificationService.NotifySuccess("Background search started.");
    }

    private void OnViewSearchResultsRequested(SearchOperation operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (operation.MatchingSequenceNumbers.Count == 0) return;
                bool match = false;
                var state = _entitySelectionState.State;
                
                if (operation.EntityType == "queue" && state.SelectedQueueName == operation.EntityPath) match = true;
                if (operation.EntityType == "subscription" && state.SelectedTopicName == operation.TopicName && state.SelectedSubscriptionName == operation.SubscriptionName) match = true;
                if (!match || operation.IsDeadLetter != state.IsViewingDLQ) return;

                // Set peeking status on PeekService if possible, or just notify our own state
                // Since PeekService has its own IsPeeking, we can't easily set it unless it exposes a setter or we just manage it here/notify.
                // Let's notify that we are starting retrieval.
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

                var token = await _peekService.GetTokenAsync(entityPath);
                if (string.IsNullOrEmpty(token)) return;

                List<ServiceBusMessage> loadedMessages;
                if (operation.EntityType == "queue")
                    loadedMessages = await _jsInterop.PeekQueueMessagesBySequenceAsync(operation.NamespaceName, operation.EntityPath, token, operation.MatchingSequenceNumbers.ToArray(), operation.IsDeadLetter);
                else
                    loadedMessages = await _jsInterop.PeekSubscriptionMessagesBySequenceAsync(operation.NamespaceName, operation.TopicName!, operation.SubscriptionName!, token, operation.MatchingSequenceNumbers.ToArray(), operation.IsDeadLetter);

                _messageListState.PeekedMessages = loadedMessages;
                _messageListState.HasPeeked = true;
                _messageListState.PeekFromSequence = 0;
                _messageListState.LastPeekOptions = null;
                _messageListState.SelectedMessage = null;
                _messageListState.NotifyUpdate();
            }
            finally
            {
                NotifyStateChanged();
            }
        });
    }

    public void Dispose()
    {
        _backgroundSearch.OnViewResultsRequested -= OnViewSearchResultsRequested;
    }
}
