using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Bussin.Backend.Models;
using Bussin.Backend.Serialization;

namespace Bussin.Backend.Functions;

public class TrackLoginFunction
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<TrackLoginFunction> _logger;

    public TrackLoginFunction(CosmosClient cosmosClient, ILogger<TrackLoginFunction> logger)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;
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
