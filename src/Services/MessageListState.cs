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
    public const int DefaultPeekCount = 50;
    public const long DefaultFromSequenceNumber = 0;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    private List<ServiceBusMessage> _peekedMessages = new();
    private string _searchTerm = "";
    private List<ServiceBusMessage>? _filteredCache;

    public List<ServiceBusMessage> PeekedMessages
    {
        get => _peekedMessages;
        set { _peekedMessages = value; _filteredCache = null; }
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set { _searchTerm = value; _filteredCache = null; }
    }

    public ServiceBusMessage? SelectedMessage { get; set; }
    public bool HasPeeked { get; set; }
    public int PeekCount { get; set; } = DefaultPeekCount;
    public int PeekFromSequence { get; set; } = (int)DefaultFromSequenceNumber;
    public PeekOptions? LastPeekOptions { get; set; }

    public int NavigatedIndex { get; set; } = -1;

    public List<ServiceBusMessage> FilteredMessages
    {
        get
        {
            if (_filteredCache != null) return _filteredCache;

            if (string.IsNullOrWhiteSpace(_searchTerm))
            {
                _filteredCache = _peekedMessages;
            }
            else
            {
                _filteredCache = _peekedMessages
                    .Where(m => (m.Body?.ToString() ?? "").Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) ||
                                (m.MessageId ?? "").Contains(_searchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            return _filteredCache;
        }
    }

    public bool ArePeekOptionsModified => LastPeekOptions != null && (
        LastPeekOptions.MaxCount != DefaultPeekCount ||
        LastPeekOptions.FromSequenceNumber != DefaultFromSequenceNumber ||
        LastPeekOptions.HasActiveFilter ||
        LastPeekOptions.PeekFromNewest
    );

    public void Clear()
    {
        _peekedMessages.Clear();
        _filteredCache = null;
        SelectedMessage = null;
        HasPeeked = false;
        PeekFromSequence = (int)DefaultFromSequenceNumber;
        LastPeekOptions = null;
        _searchTerm = "";
        NavigatedIndex = -1;
        NotifyStateChanged();
    }

    public void NotifyUpdate()
    {
        _filteredCache = null;
        NotifyStateChanged();
    }
}
