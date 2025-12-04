namespace ServiceBusExplorer.Blazor.Services;

public sealed class ConfirmModalService : IConfirmModalService
{
    public event Action? OnChange;
    
    public bool IsVisible { get; private set; }
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string? DetailMessage { get; private set; }
    public string ConfirmButtonText { get; private set; } = "Confirm";
    public string ConfirmButtonClass { get; private set; } = "btn-primary";
    public string AlternativeButtonText { get; private set; } = "";
    public string AlternativeButtonClass { get; private set; } = "btn-secondary";
    public bool IsProcessing { get; set; }
    public int? ProgressCount { get; set; }
    public object? CustomContent { get; private set; }
    
    private Func<Task>? _onConfirm;
    private Func<Task>? _onAlternativeConfirm;
    private Func<Task>? _onCancel;

    public void Show(string title, string message, string? detail, string confirmText, string confirmClass, Func<Task> onConfirm)
    {
        Title = title;
        Message = message;
        DetailMessage = detail;
        ConfirmButtonText = confirmText;
        ConfirmButtonClass = confirmClass;
        AlternativeButtonText = "";
        _onConfirm = onConfirm;
        _onAlternativeConfirm = null;
        _onCancel = null;
        CustomContent = null;
        IsVisible = true;
        IsProcessing = false;
        ProgressCount = null;
        NotifyStateChanged();
    }

    public void ShowWithAlternative(
        string title, 
        string message, 
        string? detail, 
        string confirmText, 
        string confirmClass,
        string alternativeText,
        string alternativeClass,
        Func<Task> onConfirm,
        Func<Task> onAlternativeConfirm)
    {
        Title = title;
        Message = message;
        DetailMessage = detail;
        ConfirmButtonText = confirmText;
        ConfirmButtonClass = confirmClass;
        AlternativeButtonText = alternativeText;
        AlternativeButtonClass = alternativeClass;
        _onConfirm = onConfirm;
        _onAlternativeConfirm = onAlternativeConfirm;
        _onCancel = null;
        CustomContent = null;
        IsVisible = true;
        IsProcessing = false;
        ProgressCount = null;
        NotifyStateChanged();
    }

    public void ShowWithContent<T>(
        string title, 
        string message, 
        string? detail, 
        string confirmText, 
        string confirmClass,
        T content,
        Func<Task> onConfirm)
    {
        Title = title;
        Message = message;
        DetailMessage = detail;
        ConfirmButtonText = confirmText;
        ConfirmButtonClass = confirmClass;
        AlternativeButtonText = "";
        _onConfirm = onConfirm;
        _onAlternativeConfirm = null;
        _onCancel = null;
        CustomContent = content;
        IsVisible = true;
        IsProcessing = false;
        ProgressCount = null;
        NotifyStateChanged();
    }

    public async Task ConfirmAsync()
    {
        if (_onConfirm != null)
        {
            await _onConfirm();
        }
    }

    public async Task AlternativeConfirmAsync()
    {
        if (_onAlternativeConfirm != null)
        {
            await _onAlternativeConfirm();
        }
    }

    public async Task CancelAsync()
    {
        if (_onCancel != null)
        {
            await _onCancel();
        }
        Close();
    }

    public void Close()
    {
        IsVisible = false;
        _onConfirm = null;
        _onAlternativeConfirm = null;
        _onCancel = null;
        CustomContent = null;
        IsProcessing = false;
        ProgressCount = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
