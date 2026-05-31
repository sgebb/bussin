using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Bussin.Backend.Models;
using Bussin.Backend.Serialization;
using Bussin.Backend.Services;

namespace Bussin.Backend.Functions;

public class TrackLoginFunction
{
    private readonly CosmosClient _cosmosClient;
    private readonly ITokenValidationService _tokenValidationService;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly ILogger<TrackLoginFunction> _logger;

    public TrackLoginFunction(
        CosmosClient cosmosClient,
        ITokenValidationService tokenValidationService,
        IRateLimitingService rateLimitingService,
        ILogger<TrackLoginFunction> logger)
    {
        _cosmosClient = cosmosClient;
        _tokenValidationService = tokenValidationService;
        _rateLimitingService = rateLimitingService;
        _logger = logger;
    }

    /// <summary>
    /// Tracks user logins in Cosmos DB.
    /// Secured by dynamic Microsoft Entra ID token validation and IP rate limiting for unauthenticated requests.
    /// </summary>
    [Function("TrackLogin")]
    public async Task<HttpResponseData> RunTrackLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "track-login")] HttpRequestData req)
    {
        _logger.LogInformation("Track login request received.");

        try
        {
            // 1. Try to extract and validate the Bearer token
            ClaimsPrincipal? principal = null;
            var authHeader = req.Headers.TryGetValues("Authorization", out var authValues)
                ? authValues.FirstOrDefault()
                : null;

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                principal = await _tokenValidationService.ValidateTokenAsync(token);
            }

            // 2. Apply security logic based on authentication status
            if (principal == null)
            {
                // Rate limit unauthenticated/invalid requests on IP to protect against DB/billing spam
                var ipAddress = GetClientIp(req);
                _logger.LogWarning("Unauthenticated or invalid token request from IP: {IpAddress}", ipAddress);

                if (!_rateLimitingService.IsAllowed(ipAddress, 15))
                {
                    _logger.LogWarning("Rate limit exceeded for IP: {IpAddress}", ipAddress);
                    var rateLimitResponse = req.CreateResponse((HttpStatusCode)429); // 429 Too Many Requests
                    await rateLimitResponse.WriteStringAsync("Too many requests. Please try again later.");
                    return rateLimitResponse;
                }

                // If not rate limited, return 401 Unauthorized
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync("Unauthorized. Missing or invalid Bearer token.");
                return unauthorizedResponse;
            }

            // 3. For authenticated users, extract claims securely to prevent spoofing
            var oidClaim = principal.FindFirst("oid") ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier");
            var nameClaim = principal.FindFirst("name") ?? principal.FindFirst(ClaimTypes.Name);
            var emailClaim = principal.FindFirst("preferred_username") ?? principal.FindFirst(ClaimTypes.Email) ?? principal.FindFirst("upn");

            var userId = oidClaim?.Value;
            var email = emailClaim?.Value ?? "";
            var displayName = nameClaim?.Value ?? "";

            if (string.IsNullOrEmpty(userId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Token is missing user identifier (oid).");
                return badResponse;
            }

            // Prepare record using cryptographically verified claims
            var record = new LoginRecord
            {
                id = Guid.NewGuid().ToString(),
                userId = userId,
                email = email,
                displayName = displayName,
                loginTimeUtc = DateTime.UtcNow
            };

            // Get Cosmos container configuration
            var dbName = Environment.GetEnvironmentVariable("CosmosDbDatabaseName") ?? "BussinDb";
            var containerName = Environment.GetEnvironmentVariable("CosmosDbContainerName") ?? "Logins";

            var container = _cosmosClient.GetContainer(dbName, containerName);
            await container.CreateItemAsync(record, new PartitionKey(record.userId));

            _logger.LogInformation("Login successfully recorded for verified userId: {UserId}", record.userId);

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

    private string GetClientIp(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Forwarded-For", out var values))
        {
            var ip = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
            {
                var firstIp = ip.Split(',')[0].Trim();
                if (IPAddress.TryParse(firstIp, out var parsedIp))
                {
                    return parsedIp.ToString();
                }
                var portIndex = firstIp.LastIndexOf(':');
                if (portIndex > 0)
                {
                    var withoutPort = firstIp.Substring(0, portIndex);
                    if (IPAddress.TryParse(withoutPort, out var parsedWithoutPort))
                    {
                        return parsedWithoutPort.ToString();
                    }
                }
                return firstIp;
            }
        }
        if (req.Headers.TryGetValues("X-Real-IP", out var realIpValues))
        {
            var ip = realIpValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(ip)) return ip.Trim();
        }
        return "unknown";
    }
}
