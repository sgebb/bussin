namespace Bussin.Models;

public class PeekOptions
{
    public int MaxCount { get; set; } = 50;
    public long FromSequenceNumber { get; set; } = 0;
    public string BodyFilter { get; set; } = "";
    public bool PeekFromNewest { get; set; } = false;
}
