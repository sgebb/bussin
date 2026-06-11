using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Bussin.Models;

namespace Bussin.Services;

/// <summary>
/// Scoped service managing the real-time message monitoring loop,
/// tracking received messages, seen sequences, and automatic token refresh.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly EntitySelectionState _entitySelectionState;
    private readonly PeekService _peekService;
    private readonly IServiceBusJsInteropService _jsInterop;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public bool IsMonitoring { get; private set; }
    public List<ServiceBusMessage> MonitoredMessages { get; } = new();
    public HashSet<long> SeenSequenceNumbers { get; } = new();
    public string? ErrorMessage { get; private set; }
    public int PollIntervalMs { get; set; } = 2000;

    private IJSObjectReference? _monitorController;
    private DotNetObjectReference<MessageMonitorCallback>? _callbackRef;

    public string EntityName => _entitySelectionState.State.SelectedQueueName ?? 
                                ($"{_entitySelectionState.State.SelectedTopicName}/{_entitySelectionState.State.SelectedSubscriptionName}");

    public MonitorService(
        EntitySelectionState entitySelectionState,
        PeekService peekService,
        IServiceBusJsInteropService jsInterop)
    {
        _entitySelectionState = entitySelectionState;
        _peekService = peekService;
        _jsInterop = jsInterop;
    }

    public void ClearError()
    {
        ErrorMessage = null;
        NotifyStateChanged();
    }

    public void ClearMessages()
    {
        MonitoredMessages.Clear();
        SeenSequenceNumbers.Clear();
        NotifyStateChanged();
    }

    public async Task StartMonitoringAsync(int pollIntervalMs = 2000)
    {
        var state = _entitySelectionState.State;
        var namespaceOnly = _entitySelectionState.NamespaceNameOnly;
        
        if (string.IsNullOrEmpty(namespaceOnly) || !state.HasEntitySelected)
            return;

        if (IsMonitoring)
            await StopMonitoringAsync();

        PollIntervalMs = pollIntervalMs;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            string entityPath = state.GetEntityPath();
            var token = await _peekService.GetTokenAsync(entityPath);
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "Service Bus token not available";
                NotifyStateChanged();
                return;
            }

            var callback = new MessageMonitorCallback(
                onMessage: (msg) =>
                {
                    if (msg.SequenceNumber.HasValue && SeenSequenceNumbers.Add(msg.SequenceNumber.Value))
                    {
                        MonitoredMessages.Add(msg);
                        NotifyStateChanged();
                    }
                },
                onError: (err) =>
                {
                    ErrorMessage = err;
                    NotifyStateChanged();
                },
                getFreshToken: async () =>
                {
                    // Refresh token callback
                    return await _peekService.GetTokenAsync(entityPath);
                }
            );

            _callbackRef = DotNetObjectReference.Create(callback);

            if (state.SelectedQueueName != null)
            {
                _monitorController = await _jsInterop.StartMonitoringQueueAsync(namespaceOnly, state.SelectedQueueName, token, _callbackRef);
            }
            else if (state.SelectedTopicName != null && state.SelectedSubscriptionName != null)
            {
                _monitorController = await _jsInterop.StartMonitoringSubscriptionAsync(namespaceOnly, state.SelectedTopicName, state.SelectedSubscriptionName, token, _callbackRef);
            }

            IsMonitoring = true;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start monitoring: {ex.Message}";
            Console.WriteLine($"Monitor error: {ex}");
            NotifyStateChanged();
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (_monitorController != null)
        {
            try
            {
                await _jsInterop.StopMonitoringAsync(_monitorController);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping monitor: {ex.Message}");
            }
            finally
            {
                _monitorController = null;
            }
        }

        if (_callbackRef != null)
        {
            _callbackRef.Dispose();
            _callbackRef = null;
        }

        IsMonitoring = false;
        NotifyStateChanged();
    }

    public void Dispose()
    {
        _ = StopMonitoringAsync();
    }
}
