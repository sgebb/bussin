namespace Bussin.Services;

public sealed class NotificationService : INotificationService
{
    public event Action<NotificationEventArgs>? OnNotification;

    public void NotifySuccess(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Success, id));
        Console.WriteLine($"OK: {message}");
    }

    public void NotifyError(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Error, id));
        Console.WriteLine($"ERROR: {message}");
    }

    public void NotifyInfo(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Info, id));
        Console.WriteLine($"INFO: {message}");
    }

    public void NotifyWarning(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Warning, id));
        Console.WriteLine($"WARN: {message}");
    }
}
