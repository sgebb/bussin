namespace Bussin.Models;

public class TenantInfo
{
    public required string TenantId { get; set; }
    public string? DisplayName { get; set; }
    public string? DefaultDomain { get; set; }
    public string? TenantType { get; set; } // e.g. "AAD"
}
