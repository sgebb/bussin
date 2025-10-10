namespace ServiceBusExplorer.Blazor.Services;

public sealed class NotificationService : INotificationService
{
    public event Action<string, NotificationType>? OnNotification;

    public void NotifySuccess(string message)
    {
        OnNotification?.Invoke(message, NotificationType.Success);
        Console.WriteLine($"✓ {message}");
    }

    public void NotifyError(string message)
    {
        OnNotification?.Invoke(message, NotificationType.Error);
        Console.WriteLine($"✗ {message}");
    }

    public void NotifyInfo(string message)
    {
        OnNotification?.Invoke(message, NotificationType.Info);
        Console.WriteLine($"ℹ {message}");
    }

    public void NotifyWarning(string message)
    {
        OnNotification?.Invoke(message, NotificationType.Warning);
        Console.WriteLine($"⚠ {message}");
    }
}
