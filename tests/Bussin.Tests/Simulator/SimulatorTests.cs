using Bussin.Services;
using Bussin.Services.Demo;
using Microsoft.JSInterop;
using Moq;
using Xunit;
using Bussin.Models;

namespace Bussin.Tests.Simulator;

public class SimulatorTests
{
    private readonly Mock<IJSRuntime> _jsRuntimeMock;
    private readonly DemoServiceBusJsInteropService _service;

    public SimulatorTests()
    {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _service = new DemoServiceBusJsInteropService(_jsRuntimeMock.Object);
    }

    [Fact]
    public async Task Service_InitializesSimulator_OnFirstCall()
    {
        // Arrange
        var namespaceName = "test-ns";
        var queueName = "test-q";
        
        _jsRuntimeMock
            .Setup(js => js.InvokeAsync<List<ServiceBusMessage>>(
                "ServiceBusAPI.peekQueueMessages", 
                It.IsAny<object?[]>()))
            .ReturnsAsync(new List<ServiceBusMessage>());

        // Act
        await _service.PeekQueueMessagesAsync(namespaceName, queueName, "token");

        // Assert: It should have enabled the simulator and seeded data
        _jsRuntimeMock.Verify(js => js.InvokeAsync<It.IsAnyType>(
            "ServiceBusAPI.enableSimulator", 
            It.Is<object?[]>(args => args.Contains(true))), 
            Times.Once);
            
        _jsRuntimeMock.Verify(js => js.InvokeAsync<It.IsAnyType>(
            "ServiceBusAPI.seedMockData", 
            It.IsAny<object?[]>()), 
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Send_And_Peek_Integration_Workflow()
    {
        var message = new { text = "Integration Test" };
        
        // 1. Send
        await _service.SendQueueMessageAsync("ns", "q", "token", message);

        // 2. Verify JS call was made with simulator logic
        _jsRuntimeMock.Verify(js => js.InvokeAsync<It.IsAnyType>(
            "ServiceBusAPI.sendQueueMessage", 
            It.Is<object?[]>(args => args.Contains(message))), 
            Times.Once);
    }
}
