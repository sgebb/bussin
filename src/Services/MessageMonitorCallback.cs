using Microsoft.JSInterop;
using Bussin.Models;
using System.Text.Json;

namespace Bussin.Services;

/// <summary>
/// Callback handler for continuous message monitoring
/// </summary>
public class MessageMonitorCallback
{
    private readonly Action<ServiceBusMessage> _onMessage;
    private readonly Action<string>? _onError;

    public MessageMonitorCallback(Action<ServiceBusMessage> onMessage, Action<string>? onError = null)
    {
        _onMessage = onMessage;
        _onError = onError;
    }

    [JSInvokable]
    public void OnMessage(JsonElement messageJson)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ServiceBusMessage>(messageJson.GetRawText());
            if (message != null)
            {
                _onMessage(message);
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Failed to parse message: {ex.Message}");
        }
    }

    [JSInvokable]
    public void OnError(string error)
    {
        _onError?.Invoke(error);
    }
}
