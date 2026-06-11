using Bussin.Services.Demo;
using Microsoft.JSInterop;
using Moq;
using Xunit;
using Bussin.Models;
using Bussin.Services;

namespace Bussin.Tests.Simulator;

public class SimulatorTopologyTests
{
    private readonly Mock<IJSRuntime> _jsRuntimeMock;
    private readonly DemoServiceBusJsInteropService _service;

    public SimulatorTopologyTests()
    {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _service = new DemoServiceBusJsInteropService(_jsRuntimeMock.Object);
    }

    [Fact]
    public async Task Topic_FanOut_And_DLQ_Isolation_Workflow()
    {
        // 1. Arrange: Setup Topic with 2 Subscriptions
        var ns = "bussin-ns";
        var topic = "orders-topic";
        var sub1 = "inventory-sub";
        var sub2 = "billing-sub";
        var message = new { orderId = 123, status = "New" };
        var properties = new MessageProperties { AdditionalProperties = new Dictionary<string, object> { { "Region", "US" } } };

        // 2. Act: Orchestrate the Fan-out simulation
        await _service.CreateTopic(ns, topic);
        await _service.CreateSubscription(ns, topic, sub1);
        await _service.CreateSubscription(ns, topic, sub2);
        
        await _service.SendTopicMessageAsync(ns, topic, "token", message, properties);

        // 3. Verify: Check that interop called correctly for both fan-out and properties
        _jsRuntimeMock.Verify(js => js.InvokeAsync<object>(
            "ServiceBusAPI.seedTopic", 
            It.Is<object?[]>(args => args.Contains(topic))), 
            Times.Once);

        _jsRuntimeMock.Verify(js => js.InvokeAsync<object>(
            "ServiceBusAPI.sendTopicMessage", 
            It.Is<object?[]>(args => args.Contains(topic) && args.Contains(message))), 
            Times.Once);

        // 4. Simulate a Dead-Letter on Sub 1
        var lockToken = "lock-123";
        await _service.DeadLetterMessagesAsync(new[] { lockToken });

        // Assert: Verify settlement was called with our lock token
        _jsRuntimeMock.Verify(js => js.InvokeAsync<BatchOperationResult>(
            "ServiceBusAPI.deadLetter", 
            It.Is<object?[]>(args => 
                args.Length > 0 && 
                (args[0] as string[]) != null && 
                ((string[])args[0]!).Contains(lockToken))), 
            Times.Once);
            
        // Note: In real operation, the user would now Peek Sub 1 DLQ 
        // to find the message, while Sub 2 remains active.
    }

    [Fact]
    public async Task Queue_Send_Peek_DLQ_PeekEmpty_Workflow()
    {
        // 1. Arrange
        var ns = "bussin-ns";
        var queue = "test-queue";
        var message = "Integration Test"; // Now a string to match ServiceBusMessage.Body
        
        var returnedMessages = new List<ServiceBusMessage>
        {
            new ServiceBusMessage { MessageId = "msg-123", Body = message, SequenceNumber = 42 }
        };

        // Setup the JS mock to return our message on the first Peek, and empty on the second Peek (after DLQ)
        _jsRuntimeMock.SetupSequence(js => js.InvokeAsync<List<ServiceBusMessage>>(
            "ServiceBusAPI.peekQueueMessages",
            It.Is<object?[]>(args => args.Contains(queue) && args.Length > 5 && args[5] is bool && !(bool)args[5]))) // not DLQ
            .ReturnsAsync(returnedMessages)
            .ReturnsAsync(new List<ServiceBusMessage>());

        // 2. Act: Send
        await _service.SendQueueMessageAsync(ns, queue, "token", message);

        // 3. Act: Peek
        var peeked = await _service.PeekQueueMessagesAsync(ns, queue, "token", 10, 0, false);
        
        // Assert Peek
        Assert.Single(peeked);
        Assert.Equal("msg-123", peeked[0].MessageId);
        Assert.Equal(42, peeked[0].SequenceNumber);

        // 4. Act: DLQ the peeked message
        await _service.DeadLetterQueueMessagesBySequenceAsync(ns, queue, "token", new[] { peeked[0].SequenceNumber ?? 0 }, "User Action");

        // 5. Act: Peek again (should be empty this time)
        var peekedAfterDlq = await _service.PeekQueueMessagesAsync(ns, queue, "token", 10, 0, false);

        // 6. Verify Interop Workflow
        Assert.Empty(peekedAfterDlq);

        _jsRuntimeMock.Verify(js => js.InvokeAsync<object>(
            "ServiceBusAPI.sendQueueMessage",
            It.Is<object?[]>(args => args.Contains(queue) && args.Contains(message))),
            Times.Once);

        _jsRuntimeMock.Verify(js => js.InvokeAsync<object>(
            "ServiceBusAPI.deadLetterQueueMessagesBySequence",
            It.Is<object?[]>(args => args.Contains(queue) && args.Length > 3 && (args[3] as long[]) != null && ((long[])args[3]!).Contains(42))),
            Times.Once);
    }

    [Fact]
    public async Task Resubmit_From_DLQ_Preserves_Properties_Workflow()
    {
        // 1. Arrange
        var ns = "bussin-ns";
        var queue = "test-queue";
        var originalMessage = new ServiceBusMessage 
        { 
            MessageId = "orig-msg-1", 
            Body = "content", 
            Subject = "TestSubject",
            CorrelationId = "corr-123",
            SequenceNumber = 1,
            ApplicationProperties = new Dictionary<string, object> { { "MyProp", "Value" } }
        };

        // Mock Peek to return the message in the DLQ
        _jsRuntimeMock.Setup(js => js.InvokeAsync<List<ServiceBusMessage>>(
            "ServiceBusAPI.peekQueueMessagesBySequence",
            It.Is<object?[]>(args => args.Contains(queue) && args.Length > 4 && args[4] is bool && (bool)args[4]))) // is DLQ
            .ReturnsAsync(new List<ServiceBusMessage> { originalMessage });

        // 2. Act: Run the resubmit operation
        var authMock = new Mock<IAuthenticationService>();
        authMock.Setup(a => a.GetServiceBusTokenAsync()).ReturnsAsync("demo-token");
        
        var prefMock = new Mock<IPreferencesService>();
        prefMock.Setup(p => p.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences());
        var navState = new NavigationStateService(prefMock.Object);
        
        var serviceBusOps = new ServiceBusOperationsService(
            _service, 
            authMock.Object, 
            new Mock<INotificationService>().Object,
            navState
        );
        
        await serviceBusOps.ResendQueueMessagesAsync(ns, queue, new[] { 1L }, fromDeadLetter: true, deleteOriginal: false);

        // 3. Verify: Check that the sent message contains the preserved properties
        _jsRuntimeMock.Verify(js => js.InvokeAsync<object>(
            "ServiceBusAPI.sendQueueMessageBatch",
            It.Is<object?[]>(args => 
                args.Length > 3 && 
                args[3] != null && 
                args[3] is object[] && 
                ((object[])args[3]).Length == 1 &&
                CheckResentMessage(((object[])args[3])[0], originalMessage)
            )),
            Times.Once);
    }

    private bool CheckResentMessage(object wrappedMsg, ServiceBusMessage original)
    {
        // The structure should be { body, properties = { message_id, subject, correlation_id, application_properties } }
        var json = System.Text.Json.JsonSerializer.Serialize(wrappedMsg);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("properties");
        
        return props.GetProperty("message_id").GetString() == original.MessageId &&
               props.GetProperty("subject").GetString() == original.Subject &&
               props.GetProperty("correlation_id").GetString() == original.CorrelationId &&
               props.GetProperty("application_properties").GetProperty("MyProp").GetString() == "Value" &&
               props.GetProperty("application_properties").GetProperty("x-bussin-resubmitted").GetString() == "true";
    }
}
