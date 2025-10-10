namespace ServiceBusExplorer.Blazor.Services;

public interface INotificationService
{
    event Action<string, NotificationType>? OnNotification;
    void NotifySuccess(string message);
    void NotifyError(string message);
    void NotifyInfo(string message);
    void NotifyWarning(string message);
}

public enum NotificationType
{
    Success,
    Error,
    Info,
    Warning
}
