using System;

namespace Bussin.Backend.Models;

public class LoginRecord
{
    public string id { get; set; } = "";
    public string userId { get; set; } = "";
    public string tenantId { get; set; } = "";
    public string email { get; set; } = "";
    public string displayName { get; set; } = "";
    public DateTime loginTimeUtc { get; set; }
}
