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

    /// <summary>
    /// Serves a highly optimized, static, 100% Native AOT-compliant Swagger UI.
    /// Loaded via a secure public CDN so it adds zero footprint to your compiled binary.
    /// </summary>
    [Function("SwaggerUI")]
    public async Task<HttpResponseData> RunSwagger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger")] HttpRequestData req)
    {
        _logger.LogInformation("Exposing Swagger UI.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");

        const string html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Bussin Backend API Swagger UI</title>
  <link rel=""stylesheet"" href=""https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"" />
  <link rel=""icon"" href=""https://bussin.dev/assets/favicon.svg"" type=""image/svg+xml"">
  <style>
    html { box-sizing: border-box; overflow: -y-scroll; }
    *, *:before, *:after { box-sizing: inherit; }
    body { margin:0; background: #fafafa; }
  </style>
</head>
<body>
  <div id=""swagger-ui""></div>
  <script src=""https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"" charset=""UTF-8""></script>
  <script>
    window.onload = () => {
      window.ui = SwaggerUIBundle({
        url: '/api/openapi.json',
        dom_id: '#swagger-ui',
        deepLinking: true,
        presets: [
          SwaggerUIBundle.presets.apis
        ],
        layout: ""BaseLayout""
      });
    };
  </script>
</body>
</html>";

        await response.WriteStringAsync(html);
        return response;
    }

    /// <summary>
    /// Serves the static OpenAPI 3.0 specification for the backend endpoints.
    /// 100% compatible with Native AOT with zero runtime scanning overhead.
    /// </summary>
    [Function("OpenApiSpec")]
    public async Task<HttpResponseData> RunOpenApiSpec(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.json")] HttpRequestData req)
    {
        _logger.LogInformation("Serving OpenAPI Spec.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        const string spec = @"{
  ""openapi"": ""3.0.1"",
  ""info"": {
    ""title"": ""Bussin Backend API"",
    ""description"": ""High-performance serverless backend for tracking user logins in Bussin."",
    ""version"": ""1.0.0""
  },
  ""paths"": {
    ""/api/health"": {
      ""get"": {
        ""summary"": ""Health Ping Endpoint"",
        ""description"": ""Used by UptimeRobot. Secured by Function Key parameter (?code=) to prevent billing spam."",
        ""parameters"": [
          {
            ""name"": ""code"",
            ""in"": ""query"",
            ""required"": true,
            ""schema"": {
              ""type"": ""string""
            },
            ""description"": ""Your Azure Function Default API Key.""
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Healthy status response."",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""$ref"": ""#/components/schemas/HealthStatus""
                }
              }
            }
          }
        }
      }
    },
    ""/api/track-login"": {
      ""post"": {
        ""summary"": ""Track User Login"",
        ""description"": ""Logs user profile data inside Cosmos DB upon a successful MSAL login. Anonymous access."",
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""$ref"": ""#/components/schemas/TrackLoginRequest""
              }
            }
          }
        },
        ""responses"": {
          ""200"": {
            ""description"": ""Login tracked successfully.""
          },
          ""400"": {
            ""description"": ""Invalid input payload.""
          }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""HealthStatus"": {
        ""type"": ""object"",
        ""properties"": {
          ""status"": {
            ""type"": ""string"",
            ""example"": ""healthy""
          },
          ""timestamp"": {
            ""type"": ""string"",
            ""format"": ""date-time"",
            ""example"": ""2026-05-27T15:10:45Z""
          }
        }
      },
      ""TrackLoginRequest"": {
        ""type"": ""object"",
        ""required"": [
          ""userId""
        ],
        ""properties"": {
          ""userId"": {
            ""type"": ""string"",
            ""example"": ""usr_entra_99""
          },
          ""email"": {
            ""type"": ""string"",
            ""example"": ""hello@bussin.dev""
          },
          ""displayName"": {
            ""type"": ""string"",
            ""example"": ""Developer Test""
          }
        }
      }
    }
  }
}";

        await response.WriteStringAsync(spec);
        return response;
    }
}

#endregion
