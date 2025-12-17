namespace Bussin.Services;

public interface INotificationService
{
    event Action<NotificationEventArgs>? OnNotification;
    void NotifySuccess(string message, string? id = null);
    void NotifyError(string message, string? id = null);
    void NotifyInfo(string message, string? id = null);
    void NotifyWarning(string message, string? id = null);
}

public record NotificationEventArgs(
    string Message, 
    NotificationType Type, 
    string? Id = null
);

public enum NotificationType
{
    Success,
    Error,
    Info,
    Warning
}
