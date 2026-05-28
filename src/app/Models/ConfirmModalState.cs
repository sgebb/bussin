namespace Bussin.Models;

/// <summary>
/// Encapsulates the state for a confirmation modal dialog.
/// </summary>
public sealed class ConfirmModalState
{
    public bool IsVisible { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? DetailMessage { get; set; }
    public string ConfirmButtonText { get; set; } = "Confirm";
    public string ConfirmButtonClass { get; set; } = "btn-primary";
    public string AlternativeButtonText { get; set; } = "";
    public string AlternativeButtonClass { get; set; } = "btn-secondary";
    public bool IsProcessing { get; set; }
    public int? ProgressCount { get; set; }
    
    public Func<Task>? OnConfirm { get; set; }
    public Func<Task>? OnAlternativeConfirm { get; set; }

    public void Show(string title, string message, string? detail, string confirmText, string confirmClass, Func<Task> onConfirm)
    {
        Title = title;
        Message = message;
        DetailMessage = detail;
        ConfirmButtonText = confirmText;
        ConfirmButtonClass = confirmClass;
        OnConfirm = onConfirm;
        OnAlternativeConfirm = null;
        AlternativeButtonText = "";
        IsVisible = true;
        IsProcessing = false;
        ProgressCount = null;
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
        OnConfirm = onConfirm;
        OnAlternativeConfirm = onAlternativeConfirm;
        IsVisible = true;
        IsProcessing = false;
        ProgressCount = null;
    }

    public void Close()
    {
        IsVisible = false;
        OnConfirm = null;
        OnAlternativeConfirm = null;
        IsProcessing = false;
        ProgressCount = null;
    }
}
