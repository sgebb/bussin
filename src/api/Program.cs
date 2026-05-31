using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Bussin.Backend.Serialization;
using Bussin.Backend.Services;

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

                // Register Rate Limiting and Token Validation services
                services.AddSingleton<IRateLimitingService, RateLimitingService>();
                services.AddSingleton<ITokenValidationService, TokenValidationService>();
            })
            .Build();

        await host.RunAsync();
    }
}
