namespace ServiceBusExplorer.Blazor.Models;

/// <summary>
/// Holds the current entity selection state for the Explorer page.
/// </summary>
public sealed class ExplorerState
{
    public ServiceBusNamespaceInfo? CurrentNamespace { get; private set; }
    public string? SelectedQueueName { get; private set; }
    public string? SelectedTopicName { get; private set; }
    public string? SelectedSubscriptionName { get; private set; }
    public bool IsViewingDLQ { get; set; }
    
    public string NamespaceNameOnly => CurrentNamespace?.Name ?? "";
    public string FullyQualifiedNamespace => CurrentNamespace?.FullyQualifiedNamespace ?? "";
    
    /// <summary>True if any entity (queue or topic) is selected.</summary>
    public bool HasEntitySelected => SelectedQueueName != null || SelectedTopicName != null;
    
    /// <summary>True if a queue or subscription is selected (can peek/receive messages).</summary>
    public bool HasQueueOrSubscriptionSelected => SelectedQueueName != null || SelectedSubscriptionName != null;
    
    /// <summary>True if viewing a topic's subscription.</summary>
    public bool IsSubscriptionSelected => SelectedTopicName != null && SelectedSubscriptionName != null;
    
    /// <summary>True if viewing a queue.</summary>
    public bool IsQueueSelected => SelectedQueueName != null;

    public void SetNamespace(ServiceBusNamespaceInfo? ns)
    {
        CurrentNamespace = ns;
        ClearSelection();
    }

    public void SelectQueue(string queueName)
    {
        SelectedQueueName = queueName;
        SelectedTopicName = null;
        SelectedSubscriptionName = null;
    }

    public void SelectTopic(string topicName)
    {
        SelectedQueueName = null;
        SelectedTopicName = topicName;
        SelectedSubscriptionName = null;
    }

    public void SelectSubscription(string subscriptionName)
    {
        SelectedQueueName = null;
        SelectedSubscriptionName = subscriptionName;
        // Keep SelectedTopicName as-is
    }

    public void ClearSelection()
    {
        SelectedQueueName = null;
        SelectedTopicName = null;
        SelectedSubscriptionName = null;
        IsViewingDLQ = false;
    }

    /// <summary>
    /// Gets the entity path for display (e.g., "myqueue" or "mytopic/mysubscription").
    /// </summary>
    public string GetEntityPath()
    {
        if (SelectedQueueName != null)
            return SelectedQueueName;
        if (SelectedTopicName != null && SelectedSubscriptionName != null)
            return $"{SelectedTopicName}/{SelectedSubscriptionName}";
        if (SelectedTopicName != null)
            return SelectedTopicName;
        return "";
    }
}
