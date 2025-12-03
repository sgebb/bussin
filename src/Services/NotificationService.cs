namespace ServiceBusExplorer.Blazor.Services;

public sealed class NotificationService : INotificationService
{
    public event Action<NotificationEventArgs>? OnNotification;

    public void NotifySuccess(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Success, id));
        Console.WriteLine($"✓ {message}");
    }

    public void NotifyError(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Error, id));
        Console.WriteLine($"✗ {message}");
    }

    public void NotifyInfo(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Info, id));
        Console.WriteLine($"ℹ {message}");
    }

    public void NotifyWarning(string message, string? id = null)
    {
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Warning, id));
        Console.WriteLine($"⚠ {message}");
    }
}
