using System;
using System.Collections.Generic;

namespace Bussin.Backend.Models;

public class TrackLoginResponse
{
    public string userId { get; set; } = "";
    public string displayName { get; set; } = "";
    public string email { get; set; } = "";
    public string tenantId { get; set; } = "";
    public string tier { get; set; } = "Free";
    public List<string> features { get; set; } = new();
}
