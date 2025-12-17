namespace Bussin.Services;

/// <summary>
/// Service for managing confirmation modal dialogs.
/// </summary>
public interface IConfirmModalService
{
    /// <summary>Event raised when modal state changes.</summary>
    event Action? OnChange;
    
    bool IsVisible { get; }
    string Title { get; }
    string Message { get; }
    string? DetailMessage { get; }
    string ConfirmButtonText { get; }
    string ConfirmButtonClass { get; }
    string AlternativeButtonText { get; }
    string AlternativeButtonClass { get; }
    bool IsProcessing { get; set; }
    int? ProgressCount { get; set; }
    
    /// <summary>Custom content to render inside the modal (e.g., checkboxes).</summary>
    object? CustomContent { get; }

    void Show(string title, string message, string? detail, string confirmText, string confirmClass, Func<Task> onConfirm);
    
    void ShowWithAlternative(
        string title, 
        string message, 
        string? detail, 
        string confirmText, 
        string confirmClass,
        string alternativeText,
        string alternativeClass,
        Func<Task> onConfirm,
        Func<Task> onAlternativeConfirm);

    void ShowWithContent<T>(
        string title, 
        string message, 
        string? detail, 
        string confirmText, 
        string confirmClass,
        T content,
        Func<Task> onConfirm);

    Task ConfirmAsync();
    Task AlternativeConfirmAsync();
    Task CancelAsync();
    void Close();
}
