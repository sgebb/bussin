using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Azure.Identity;

namespace Bussin.Backend;

public static class Program
{
    public static async Task Main()
    {
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(services =>
            {
                // Register CosmosClient as a Singleton AOT-safely
                services.AddSingleton(sp =>
                {
                    var options = new CosmosClientOptions
                    {
                        Serializer = new CosmosSystemTextJsonSerializer(BussinJsonContext.Default)
                    };

                    var connectionString = Environment.GetEnvironmentVariable("CosmosDbConnectionString");
                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        // Fallback to connection string (useful for local development / emulator)
                        return new CosmosClient(connectionString, options);
                    }

                    var endpoint = Environment.GetEnvironmentVariable("CosmosDbEndpoint");
                    if (string.IsNullOrEmpty(endpoint))
                    {
                        throw new InvalidOperationException("Either CosmosDbConnectionString or CosmosDbEndpoint configuration must be provided.");
                    }

                    // Entra ID passwordless authentication (best practice for production)
                    return new CosmosClient(endpoint, new DefaultAzureCredential(), options);
                });
            })
            .Build();

        await host.RunAsync();
    }
}

#region Models & Serialization Context

public class TrackLoginRequest
{
    public string userId { get; set; } = "";
    public string email { get; set; } = "";
    public string displayName { get; set; } = "";
}

public class LoginRecord
{
    public string id { get; set; } = "";
    public string userId { get; set; } = "";
    public string email { get; set; } = "";
    public string displayName { get; set; } = "";
    public DateTime loginTimeUtc { get; set; }
}

[JsonSerializable(typeof(TrackLoginRequest))]
[JsonSerializable(typeof(LoginRecord))]
[JsonSerializable(typeof(HealthStatus))]
internal partial class BussinJsonContext : JsonSerializerContext
{
}

public class HealthStatus
{
    public string status { get; set; } = "";
    public string timestamp { get; set; } = "";
}

#endregion

#region Custom Cosmos Serializer (AOT-Compliant)

public class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonSerializerContext _context;

    public CosmosSystemTextJsonSerializer(JsonSerializerContext context)
    {
        _context = context;
    }

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.Length == 0) return default!;
            return (T)JsonSerializer.Deserialize(stream, typeof(T), _context)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var memoryStream = new MemoryStream();
        JsonSerializer.Serialize(memoryStream, input, typeof(T), _context);
        memoryStream.Position = 0;
        return memoryStream;
    }
}

#endregion

#region Functions

public class TrackerFunctions
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<TrackerFunctions> _logger;

    public TrackerFunctions(CosmosClient cosmosClient, ILogger<TrackerFunctions> logger)
    {
        _cosmosClient = cosmosClient;
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

    /// <summary>
    /// Tracks user logins in Cosmos DB.
    /// </summary>
    [Function("TrackLogin")]
    public async Task<HttpResponseData> RunTrackLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "track-login")] HttpRequestData req)
    {
        _logger.LogInformation("Track login request received.");

        try
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Request body cannot be empty.");
                return badResponse;
            }

            var request = (TrackLoginRequest?)JsonSerializer.Deserialize(body, typeof(TrackLoginRequest), BussinJsonContext.Default);
            if (request == null || string.IsNullOrEmpty(request.userId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid login payload or missing userId.");
                return badResponse;
            }

            // Prepare record
            var record = new LoginRecord
            {
                id = Guid.NewGuid().ToString(),
                userId = request.userId,
                email = request.email,
                displayName = request.displayName,
                loginTimeUtc = DateTime.UtcNow
            };

            // Get Cosmos container configuration
            var dbName = Environment.GetEnvironmentVariable("CosmosDbDatabaseName") ?? "BussinDb";
            var containerName = Environment.GetEnvironmentVariable("CosmosDbContainerName") ?? "Logins";

            var container = _cosmosClient.GetContainer(dbName, containerName);
            await container.CreateItemAsync(record, new PartitionKey(record.userId));

            _logger.LogInformation("Login successfully recorded for userId: {UserId}", record.userId);

            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteStringAsync("Login tracked successfully.");
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during track-login execution.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Internal error: {ex.Message}");
            return errorResponse;
        }
    }
}

#endregion
