namespace Bussin.Models;

public class PeekOptions
{
    public int MaxCount { get; set; } = 50;
    public long FromSequenceNumber { get; set; } = 0;
    public string BodyFilter { get; set; } = "";
    public string MessageIdFilter { get; set; } = "";
    public string SubjectFilter { get; set; } = "";
    public bool PeekFromNewest { get; set; } = false;
    
    /// <summary>
    /// Returns true if any filter is active
    /// </summary>
    public bool HasActiveFilter => 
        !string.IsNullOrWhiteSpace(BodyFilter) || 
        !string.IsNullOrWhiteSpace(MessageIdFilter) || 
        !string.IsNullOrWhiteSpace(SubjectFilter);
}
