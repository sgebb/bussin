using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Bussin.Backend.Functions;

public class SwaggerFunction
{
    private readonly ILogger<SwaggerFunction> _logger;

    public SwaggerFunction(ILogger<SwaggerFunction> logger)
    {
        _logger = logger;
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
