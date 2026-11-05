using Bussin.Models;
using Bussin.Services;
using Moq;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class ResendMessagesTests
{
    private const string Ns = "bussin-ns";
    private const string Queue = "orders";

    private readonly Mock<IServiceBusJsInteropService> _js = new();
    private readonly ServiceBusOperationsService _ops;

    public ResendMessagesTests()
    {
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.GetServiceBusTokenAsync()).ReturnsAsync("token");
        var prefs = new Mock<IPreferencesService>();
        prefs.Setup(p => p.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences());
        _ops = new ServiceBusOperationsService(_js.Object, new Mock<INotificationService>().Object,
            new ServiceBusTokenProvider(new NavigationStateService(prefs.Object), auth.Object));

        _js.Setup(j => j.PeekQueueMessagesBySequenceAsync(Ns, Queue, It.IsAny<string>(), It.IsAny<long[]>(), true))
            .ReturnsAsync([new ServiceBusMessage { SequenceNumber = 1, Body = "a" }, new ServiceBusMessage { SequenceNumber = 2, Body = "b" }]);
    }

    [Fact]
    public async Task OriginalThatCouldNotBeRemoved_IsReportedWithItsSequenceNumber()
    {
        _js.Setup(j => j.DeleteQueueMessagesBySequenceAsync(Ns, Queue, It.IsAny<string>(), It.IsAny<long[]>(), true, null))
            .ReturnsAsync(new SettleBySequenceResult { Settled = [1], NotFound = [2] });

        var result = await _ops.ResendQueueMessagesAsync(Ns, Queue, [1, 2, 3], fromDeadLetter: true);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(2, result.FailureCount);
        Assert.Contains(result.Errors, e => e.SequenceNumber == 2 && e.Error.StartsWith("Resent, but"));
        Assert.Contains(result.Errors, e => e.SequenceNumber == 3 && e.Error.StartsWith("Not found"));
    }

    [Fact]
    public async Task RemovalThatThrows_IsReportedAsNotRemoved_NotAsFailedResend()
    {
        _js.Setup(j => j.DeleteQueueMessagesBySequenceAsync(Ns, Queue, It.IsAny<string>(), It.IsAny<long[]>(), true, null))
            .ThrowsAsync(new InvalidOperationException("connection lost"));

        var result = await _ops.ResendQueueMessagesAsync(Ns, Queue, [1, 2], fromDeadLetter: true);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal([1L, 2L], result.Errors.Select(e => e.SequenceNumber!.Value));
        Assert.All(result.Errors, e => Assert.Contains("connection lost", e.Error));
    }
}
