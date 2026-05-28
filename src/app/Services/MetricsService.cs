using Azure.Core;
using Bussin.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Bussin.Services;

public interface IMetricsService
{
    /// <summary>
    /// Gets message metrics for a Service Bus namespace over the specified time period.
    /// </summary>
    Task<EntityMetrics> GetNamespaceMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        TimeSpan period,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Gets message metrics for a specific queue over the specified time period.
    /// </summary>
    Task<EntityMetrics> GetQueueMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string queueName,
        TimeSpan period,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Gets message metrics for a specific topic over the specified time period.
    /// </summary>
    Task<EntityMetrics> GetTopicMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string topicName,
        TimeSpan period,
        CancellationToken cancellationToken = default);
}

public sealed class MetricsService : IMetricsService
{
    private readonly HttpClient _httpClient;
    private const string AzureManagementScope = "https://management.azure.com/.default";
    private const string ApiVersion = "2023-10-01";

    public MetricsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<EntityMetrics> GetNamespaceMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        var resourceId = $"/subscriptions/{namespaceInfo.SubscriptionId}/resourceGroups/{namespaceInfo.ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{namespaceInfo.Name}";
        return await GetMetricsAsync(credential, resourceId, null, period, cancellationToken);
    }

    public async Task<EntityMetrics> GetQueueMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string queueName,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        var resourceId = $"/subscriptions/{namespaceInfo.SubscriptionId}/resourceGroups/{namespaceInfo.ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{namespaceInfo.Name}";
        return await GetMetricsAsync(credential, resourceId, queueName, period, cancellationToken);
    }

    public async Task<EntityMetrics> GetTopicMetricsAsync(
        TokenCredential credential,
        ServiceBusNamespaceInfo namespaceInfo,
        string topicName,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        var resourceId = $"/subscriptions/{namespaceInfo.SubscriptionId}/resourceGroups/{namespaceInfo.ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{namespaceInfo.Name}";
        return await GetMetricsAsync(credential, resourceId, topicName, period, cancellationToken);
    }

    private async Task<EntityMetrics> GetMetricsAsync(
        TokenCredential credential,
        string resourceId,
        string? entityName,
        TimeSpan period,
        CancellationToken cancellationToken)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;
        
        try
        {
            // Get token for Azure Management API
            var tokenRequest = new TokenRequestContext(new[] { AzureManagementScope });
            var accessToken = await credential.GetTokenAsync(tokenRequest, cancellationToken);

            // Build the metrics query URL
            // Azure Monitor Metrics API: https://learn.microsoft.com/en-us/rest/api/monitor/metrics/list
            var metricNames = "IncomingMessages,OutgoingMessages,Size,ActiveMessages,DeadletteredMessages,ScheduledMessages";
            var timespan = $"{startTime:yyyy-MM-ddTHH:mm:ssZ}/{endTime:yyyy-MM-ddTHH:mm:ssZ}";
            
            var url = $"https://management.azure.com{resourceId}/providers/Microsoft.Insights/metrics" +
                      $"?api-version={ApiVersion}" +
                      $"&metricnames={Uri.EscapeDataString(metricNames)}" +
                      $"&timespan={Uri.EscapeDataString(timespan)}" +
                      $"&aggregation=Total,Maximum" +
                      $"&interval=PT1H";  // 1-hour granularity

            // Add entity filter if specified
            if (!string.IsNullOrEmpty(entityName))
            {
                url += $"&$filter=EntityName eq '{Uri.EscapeDataString(entityName)}'";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new EntityMetrics
                {
                    IsAvailable = false,
                    ErrorMessage = $"Failed to retrieve metrics: {response.StatusCode} - {errorBody}",
                    PeriodStart = startTime,
                    PeriodEnd = endTime
                };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseMetricsResponse(content, startTime, endTime);
        }
        catch (Exception ex)
        {
            return new EntityMetrics
            {
                IsAvailable = false,
                ErrorMessage = PermissionErrorHelper.FormatError(ex.Message, "retrieve metrics for"),
                PeriodStart = startTime,
                PeriodEnd = endTime
            };
        }
    }

    private static EntityMetrics ParseMetricsResponse(string jsonContent, DateTime startTime, DateTime endTime)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            long incomingMessages = 0;
            long outgoingMessages = 0;
            long sizeInBytes = 0;
            long activeMessages = 0;
            long deadLetteredMessages = 0;
            long scheduledMessages = 0;

            if (root.TryGetProperty("value", out var metrics))
            {
                foreach (var metric in metrics.EnumerateArray())
                {
                    if (!metric.TryGetProperty("name", out var nameElement) ||
                        !nameElement.TryGetProperty("value", out var metricName))
                        continue;

                    var name = metricName.GetString() ?? "";
                    
                    if (!metric.TryGetProperty("timeseries", out var timeseries))
                        continue;

                    // Sum up all the values in the timeseries
                    foreach (var series in timeseries.EnumerateArray())
                    {
                        if (!series.TryGetProperty("data", out var dataPoints))
                            continue;

                        foreach (var dp in dataPoints.EnumerateArray())
                        {
                            // Determine correct aggregation based on metric type
                            // Flow metrics (Incoming/Outgoing) should use Total (Sum)
                            // State metrics (Active, Size, etc) should use Maximum (Peak) or Average
                            
                            double value = 0;
                            bool isFlowMetric = name == "IncomingMessages" || name == "OutgoingMessages";

                            if (isFlowMetric)
                            {
                                if (dp.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                                    value = total.GetDouble();
                            }
                            else
                            {
                                // For state metrics, prefer Maximum, then Average
                                if (dp.TryGetProperty("maximum", out var max) && max.ValueKind == JsonValueKind.Number)
                                    value = max.GetDouble();
                                else if (dp.TryGetProperty("average", out var avg) && avg.ValueKind == JsonValueKind.Number)
                                    value = avg.GetDouble();
                                // Fallback to total if nothing else exists (though unlikely for state metrics to only have total)
                                else if (dp.TryGetProperty("total", out var tot) && tot.ValueKind == JsonValueKind.Number)
                                    value = tot.GetDouble();
                            }

                            switch (name)
                            {
                                case "IncomingMessages":
                                    incomingMessages += (long)value;
                                    break;
                                case "OutgoingMessages":
                                    outgoingMessages += (long)value;
                                    break;
                                case "Size":
                                    // Take the maximum size seen
                                    sizeInBytes = Math.Max(sizeInBytes, (long)value);
                                    break;
                                case "ActiveMessages":
                                    activeMessages = Math.Max(activeMessages, (long)value);
                                    break;
                                case "DeadletteredMessages":
                                    deadLetteredMessages = Math.Max(deadLetteredMessages, (long)value);
                                    break;
                                case "ScheduledMessages":
                                    scheduledMessages = Math.Max(scheduledMessages, (long)value);
                                    break;
                            }
                        }
                    }
                }
            }

            return new EntityMetrics
            {
                IncomingMessages = incomingMessages,
                OutgoingMessages = outgoingMessages,
                SizeInBytes = sizeInBytes,
                ActiveMessages = activeMessages,
                DeadLetteredMessages = deadLetteredMessages,
                ScheduledMessages = scheduledMessages,
                PeriodStart = startTime,
                PeriodEnd = endTime,
                IsAvailable = true
            };
        }
        catch (Exception ex)
        {
            return new EntityMetrics
            {
                IsAvailable = false,
                ErrorMessage = $"Failed to parse metrics response: {ex.Message}",
                PeriodStart = startTime,
                PeriodEnd = endTime
            };
        }
    }
}
