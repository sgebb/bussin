using Bussin.Models;
using Bussin.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class BackgroundResubmitServiceTests
{
    private const string Ns = "bussin-ns";
    private const string Queue = "orders";

    private readonly Mock<IServiceBusJsInteropService> _js = new();
    private readonly Mock<IServiceBusOperationsService> _ops = new();
    private readonly BackgroundResubmitService _service;

    public BackgroundResubmitServiceTests()
    {
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.GetServiceBusTokenAsync()).ReturnsAsync("token");
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences());

        var services = new ServiceCollection()
            .AddSingleton(_js.Object)
            .AddSingleton(_ops.Object)
            .AddSingleton(new ServiceBusTokenProvider(new NavigationStateService(prefs.Object), auth.Object))
            .BuildServiceProvider();
        _service = new BackgroundResubmitService(services.GetRequiredService<IServiceScopeFactory>(), new Mock<INotificationService>().Object);

        _js.SetupSequence(j => j.ReceiveAndLockQueueMessagesAsync(Ns, Queue, It.IsAny<string>(), It.IsAny<int>(), true, It.IsAny<int>(), null))
            .ReturnsAsync([new ServiceBusMessage { SequenceNumber = 1, LockToken = "t1" }, new ServiceBusMessage { SequenceNumber = 2, LockToken = "t2" }])
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task WithDelete_SendsCopiesThenCompletesTheLockedOriginals()
    {
        var order = new List<string>();
        _ops.Setup(o => o.SendResubmitCopiesAsync(Ns, Queue, null, It.IsAny<IReadOnlyList<ServiceBusMessage>>(), true))
            .Callback(() => order.Add("send")).Returns(Task.CompletedTask);
        _ops.Setup(o => o.CompleteMessagesAsync(new[] { "t1", "t2" }))
            .Callback(() => order.Add("complete")).ReturnsAsync(new BatchOperationResult { SuccessCount = 2 });

        var operation = await RunAsync();

        Assert.Equal(ResubmitStatus.Completed, operation.Status);
        Assert.Equal(2, operation.MessagesResubmitted);
        Assert.Equal(["send", "complete"], order);
    }

    [Fact]
    public async Task WithDelete_FailedSend_PutsTheBatchBackAndCompletesNothing()
    {
        _ops.Setup(o => o.SendResubmitCopiesAsync(Ns, Queue, null, It.IsAny<IReadOnlyList<ServiceBusMessage>>(), true))
            .ThrowsAsync(new InvalidOperationException("send failed"));

        var operation = await RunAsync();

        Assert.Equal(ResubmitStatus.Failed, operation.Status);
        Assert.Equal("send failed", operation.ErrorMessage);
        _ops.Verify(o => o.AbandonMessagesAsync(new[] { "t1", "t2" }), Times.Once);
        _ops.Verify(o => o.CompleteMessagesAsync(It.IsAny<string[]>()), Times.Never);
    }

    private async Task<ResubmitOperation> RunAsync()
    {
        var id = await _service.StartResubmitAsync(Ns, "queue", Queue, deleteAfterResubmit: true);
        var operation = _service.ActiveOperations.Single(o => o.Id == id);
        for (var i = 0; i < 100 && operation.Status == ResubmitStatus.Running; i++)
        {
            await Task.Delay(50);
        }
        return operation;
    }
}
