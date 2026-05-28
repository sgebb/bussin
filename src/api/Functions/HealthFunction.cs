using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Bussin.Backend.Models;
using Bussin.Backend.Serialization;

namespace Bussin.Backend.Functions;

public class HealthFunction
{
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(ILogger<HealthFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Health Ping Endpoint used by UptimeRobot. 
    /// Secured by AuthorizationLevel.Function to prevent billing/DDOS spam.
    /// Does not connect to Cosmos DB to keep RU consumption at absolute zero.
    /// </summary>
    [Function("Health")]
    public async Task<HttpResponseData> RunHealth(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "health")] HttpRequestData req)
    {
        _logger.LogInformation("Health check ping received.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        var status = new HealthStatus 
        { 
            status = "healthy", 
            timestamp = DateTime.UtcNow.ToString("o") 
        };

        var json = JsonSerializer.Serialize(status, typeof(HealthStatus), BussinJsonContext.Default);
        await response.WriteStringAsync(json);

        return response;
    }
}
