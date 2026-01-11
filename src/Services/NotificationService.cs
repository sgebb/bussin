namespace Bussin.Services;

public sealed class NotificationService : INotificationService
{
    private readonly List<StoredNotification> _notifications = new();
    private readonly TimeSpan _notificationLifetime = TimeSpan.FromMinutes(10);
    
    public event Action<NotificationEventArgs>? OnNotification;
    public event Action? OnNotificationsChanged;
    
    public IReadOnlyList<StoredNotification> RecentNotifications
    {
        get
        {
            CleanupOldNotifications();
            return _notifications.AsReadOnly();
        }
    }
    
    public int UnreadCount => _notifications.Count(n => !n.IsRead);

    public void NotifySuccess(string message, string? id = null)
    {
        AddNotification(message, NotificationType.Success, id);
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Success, id));
        Console.WriteLine($"OK: {message}");
    }

    public void NotifyError(string message, string? id = null)
    {
        AddNotification(message, NotificationType.Error, id);
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Error, id));
        Console.WriteLine($"ERROR: {message}");
    }

    public void NotifyInfo(string message, string? id = null)
    {
        AddNotification(message, NotificationType.Info, id);
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Info, id));
        Console.WriteLine($"INFO: {message}");
    }

    public void NotifyWarning(string message, string? id = null)
    {
        AddNotification(message, NotificationType.Warning, id);
        OnNotification?.Invoke(new NotificationEventArgs(message, NotificationType.Warning, id));
        Console.WriteLine($"WARN: {message}");
    }
    
    public void MarkAllAsRead()
    {
        foreach (var notification in _notifications)
        {
            notification.IsRead = true;
        }
        OnNotificationsChanged?.Invoke();
    }
    
    public void ClearNotification(string notificationId)
    {
        var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
        if (notification != null)
        {
            _notifications.Remove(notification);
            OnNotificationsChanged?.Invoke();
        }
    }
    
    public void ClearAllNotifications()
    {
        _notifications.Clear();
        OnNotificationsChanged?.Invoke();
    }
    
    private void AddNotification(string message, NotificationType type, string? id)
    {
        // Don't track purge progress notifications (they're handled by TasksPanel)
        if (message.Contains("Purging") && message.Contains("messages deleted"))
        {
            return;
        }
        
        var notification = new StoredNotification
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Message = message,
            Type = type,
            Timestamp = DateTime.Now
        };
        
        _notifications.Add(notification);
        OnNotificationsChanged?.Invoke();
    }
    
    private void CleanupOldNotifications()
    {
        var cutoff = DateTime.Now - _notificationLifetime;
        var removed = _notifications.RemoveAll(n => n.Timestamp < cutoff);
        
        if (removed > 0)
        {
            OnNotificationsChanged?.Invoke();
        }
    }
}

public class StoredNotification
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; } = false;
    
    public string TimeAgo
    {
        get
        {
            var elapsed = DateTime.Now - Timestamp;
            if (elapsed.TotalSeconds < 60)
                return "just now";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes}m ago";
            return $"{(int)elapsed.TotalHours}h ago";
        }
    }
}
