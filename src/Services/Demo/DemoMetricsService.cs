using Azure.Core;
using Bussin.Models;

namespace Bussin.Services.Demo;

public class DemoMetricsService : IMetricsService
{
    private readonly Random _random = new();

    public Task<EntityMetrics> GetNamespaceMetricsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, TimeSpan period, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateRandomMetrics(period));
    }

    public Task<EntityMetrics> GetQueueMetricsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string queueName, TimeSpan period, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateRandomMetrics(period));
    }

    public Task<EntityMetrics> GetTopicMetricsAsync(TokenCredential credential, ServiceBusNamespaceInfo namespaceInfo, string topicName, TimeSpan period, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateRandomMetrics(period));
    }

    private EntityMetrics CreateRandomMetrics(TimeSpan period)
    {
        var now = DateTime.UtcNow;
        return new EntityMetrics
        {
            IsAvailable = true,
            PeriodStart = now - period,
            PeriodEnd = now,
            IncomingMessages = _random.Next(100, 1000),
            OutgoingMessages = _random.Next(50, 900),
            ActiveMessages = _random.Next(0, 100),
            DeadLetteredMessages = _random.Next(0, 10),
            ScheduledMessages = _random.Next(0, 5),
            SizeInBytes = _random.Next(1024, 1024 * 1024)
        };
    }
}
