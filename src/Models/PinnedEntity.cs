using System.Text.Json.Serialization;

namespace Bussin.Models;

/// <summary>A favorite entity within a namespace: a queue, a topic, or one of a topic's subscriptions.</summary>
public sealed record PinnedEntity(string? Queue = null, string? Topic = null, string? Subscription = null)
{
    [JsonIgnore]
    public string DisplayName => Queue ?? (Subscription != null ? $"{Topic}/{Subscription}" : Topic ?? "");
}
