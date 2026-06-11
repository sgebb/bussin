using System;
using System.Collections.Generic;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Scoped state container holding the current loaded messages, 
/// selected message, and pagination/filtering options.
/// </summary>
public sealed class MessageListState
{
    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public List<ServiceBusMessage> PeekedMessages { get; set; } = new();
    public ServiceBusMessage? SelectedMessage { get; set; }
    public bool HasPeeked { get; set; }
    public int PeekCount { get; set; } = 50;
    public int PeekFromSequence { get; set; } = 0;
    public PeekOptions? LastPeekOptions { get; set; }

    public string SearchTerm { get; set; } = "";
    public int NavigatedIndex { get; set; } = -1;

    public List<ServiceBusMessage> FilteredMessages
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchTerm))
                return PeekedMessages;

            return PeekedMessages
                .Where(m => (m.Body?.ToString() ?? "").Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                            (m.MessageId ?? "").Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public bool ArePeekOptionsModified => LastPeekOptions != null && (
        LastPeekOptions.MaxCount != 50 ||
        LastPeekOptions.FromSequenceNumber != 0 ||
        LastPeekOptions.HasActiveFilter ||
        LastPeekOptions.PeekFromNewest
    );

    public void Clear()
    {
        PeekedMessages.Clear();
        SelectedMessage = null;
        HasPeeked = false;
        PeekFromSequence = 0;
        LastPeekOptions = null;
        SearchTerm = "";
        NavigatedIndex = -1;
        NotifyStateChanged();
    }

    public void NotifyUpdate()
    {
        NotifyStateChanged();
    }
}
