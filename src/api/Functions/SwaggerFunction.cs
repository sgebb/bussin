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
        layout: ""BaseLayout"",
        oauth2RedirectUrl: window.location.origin + '/api/oauth2-redirect'
      });

      window.ui.initOAuth({
        clientId: '36145d65-2256-48e6-a5f6-ae8fde23c103',
        appName: 'Bussin Backend',
        usePkceWithAuthorizationCodeGrant: false
      });
    };
  </script>
</body>
</html>";

        await response.WriteStringAsync(html);
        return response;
    }

    /// <summary>
    /// Serves the local, secure OAuth2 redirect page for Swagger UI authorization callback.
    /// Eliminates external CDN dependencies and resolves Entra ID path validation restrictions.
    /// </summary>
    [Function("SwaggerOAuth2Redirect")]
    public async Task<HttpResponseData> RunOAuth2Redirect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth2-redirect")] HttpRequestData req)
    {
        _logger.LogInformation("Serving Swagger OAuth2 Redirect callback page.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");

        const string html = @"<!doctype html>
<html lang=""en-US"">
<head>
    <title>Swagger UI: OAuth2 Redirect</title>
</head>
<body onload=""run()"">
  <script>
    'use strict';
    function run () {
      var oauth2 = window.opener.swaggerUIRedirectOauth2;
      var sentState = oauth2.state;
      var isValid = true;
      var qpArr = window.location.search.substring(1).split('&');
      if (qpArr.length === 1 && qpArr[0] === '') {
        qpArr = window.location.hash.substring(1).split('&');
      }
      var qpMap = {};

      qpArr.forEach(function (v) {
        var arr = v.split('=');
        qpMap[arr[0]] = decodeURIComponent(arr[1]);
      });

      if (qpMap.state !== sentState) {
        var err = 'The state parameter does not match the state from the authorization request.';
        oauth2.errCb(err);
      } else {
        oauth2.callback({auth: oauth2.auth, token: qpMap, isValid: isValid});
      }
      window.close();
    }
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
        ""description"": ""Logs user profile data inside Cosmos DB upon a successful MSAL login."",
        ""security"": [
          {
            ""oauth2"": [
              ""api://36145d65-2256-48e6-a5f6-ae8fde23c103/.default""
            ]
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Login tracked successfully.""
          },
          ""401"": {
            ""description"": ""Unauthorized. Missing or invalid Bearer token.""
          },
          ""429"": {
            ""description"": ""Too Many Requests. Rate limit exceeded.""
          }
        }
      }
    }
  },
  ""components"": {
    ""securitySchemes"": {
      ""oauth2"": {
        ""type"": ""oauth2"",
        ""description"": ""Microsoft Entra ID login flow for Bussin App"",
        ""flows"": {
          ""implicit"": {
            ""authorizationUrl"": ""https://login.microsoftonline.com/common/oauth2/v2.0/authorize"",
            ""scopes"": {
              ""api://36145d65-2256-48e6-a5f6-ae8fde23c103/.default"": ""Default access to the Bussin API""
            }
          }
        }
      }
    },
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
      }
    }
  }
}";

        await response.WriteStringAsync(spec);
        return response;
    }
}
