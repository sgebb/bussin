using Microsoft.JSInterop;

namespace Bussin.Services;

/// <summary>
/// Callback handler for search progress updates
/// </summary>
public class SearchProgressCallback
{
    private readonly Action<int, int, List<long>> _onProgress;

    public SearchProgressCallback(Action<int, int, List<long>> onProgress)
    {
        _onProgress = onProgress;
    }

    /// <summary>
    /// Called from JS with progress updates
    /// </summary>
    /// <param name="scannedCount">Number of messages scanned so far</param>
    /// <param name="matchCount">Number of matches found so far</param>
    /// <param name="newMatches">New sequence numbers that matched in this batch</param>
    [JSInvokable]
    public void OnProgress(int scannedCount, int matchCount, long[] newMatches)
    {
        _onProgress(scannedCount, matchCount, newMatches.ToList());
    }
}
