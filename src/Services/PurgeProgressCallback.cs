using Microsoft.JSInterop;

namespace ServiceBusExplorer.Blazor.Services;

/// <summary>
/// Callback handler for purge progress updates
/// </summary>
public class PurgeProgressCallback
{
    private readonly Action<int> _onProgress;

    public PurgeProgressCallback(Action<int> onProgress)
    {
        _onProgress = onProgress;
    }

    [JSInvokable]
    public void OnProgress(int count)
    {
        _onProgress(count);
    }
}
