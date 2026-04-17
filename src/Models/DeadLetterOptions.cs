using System.Text.Json.Serialization;

namespace Bussin.Models;

/// <summary>
/// Options for dead lettering messages
/// </summary>
public class DeadLetterOptions
{
    [JsonPropertyName("deadLetterReason")]
    public string? DeadLetterReason { get; set; }
    
    [JsonPropertyName("deadLetterErrorDescription")]
    public string? DeadLetterErrorDescription { get; set; }
}
