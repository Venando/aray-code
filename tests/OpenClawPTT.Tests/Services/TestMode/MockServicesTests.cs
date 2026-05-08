using Moq;
using OpenClawPTT.Services;
using OpenClawPTT.Services.TestMode;
using Xunit;

namespace OpenClawPTT.Tests.Services.TestMode;

/// <summary>
/// Slow tests — these use real async timeouts and are single-threaded.
/// Filter with --filter "Category!=Slow" to skip during development.
/// </summary>
[Trait("Category", "Slow")]
public class MockServicesTests
{
    private readonly Mock<IColorConsole> _mockConsole;

    public MockServicesTests()
    {
        _mockConsole = new Mock<IColorConsole>();
    }

    #region MockGatewayService

    [Fact]
    public void MockGatewayService_Constructor_SetsProperties()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public async Task MockGatewayService_ConnectAsync_SimulatesConnection()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);

        await service.ConnectAsync();

        _mockConsole.Verify(x => x.PrintInfo(It.Is<string>(s => s.Contains("Simulating gateway"))), Times.Once);
        _mockConsole.Verify(x => x.PrintInfo(It.Is<string>(s => s.Contains("Connected"))), Times.Once);
    }

    [Fact]
    public async Task MockGatewayService_SendTextAsync_RaisesAgentReplyFull()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);
        string? receivedReply = null;
        service.AgentReplyFull += reply => receivedReply = reply;

        await service.SendTextAsync("Hello");

        Assert.NotNull(receivedReply);
        Assert.NotEmpty(receivedReply);
    }

    [Fact]
    public async Task MockGatewayService_SendTextAsync_RaisesAgentReplyDeltaEvents()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);
        var deltaStarted = false;
        var deltaEnded = false;
        var deltas = new List<string>();

        service.AgentReplyDeltaStart += () => deltaStarted = true;
        service.AgentReplyDeltaEnd += () => deltaEnded = true;
        service.AgentReplyDelta += delta => deltas.Add(delta);

        await service.SendTextAsync("Hello");

        Assert.True(deltaStarted);
        Assert.True(deltaEnded);
        Assert.True(deltas.Count > 0);
    }

    [Fact]
    public async Task MockGatewayService_SendTextAsync_ThinkingMessage_RaisesThinkingEvent()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);
        string? thinking = null;
        service.AgentThinking += t => thinking = t;

        // Send a message with a question mark to trigger thinking
        await service.SendTextAsync("Can you help me with something?");

        Assert.NotNull(thinking);
    }

    [Fact]
    public async Task MockGatewayService_FetchSessionHistoryAsync_ReturnsHistory()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);

        var history = await service.FetchSessionHistoryAsync("test-session");

        Assert.NotNull(history);
        Assert.True(history.Count > 0);
    }

    [Fact]
    public void MockGatewayService_GetMockAgents_ReturnsAgents()
    {
        var service = new MockGatewayService(TestScenarios.MultiAgent, _mockConsole.Object);

        var agents = service.GetMockAgents();

        Assert.Equal(3, agents.Count);
    }

    [Fact]
    public void MockGatewayService_Dispose_DoesNotThrow()
    {
        var service = new MockGatewayService(TestScenarios.BasicChat, _mockConsole.Object);

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    #endregion

    #region MockAudioService

    [Fact]
    public void MockAudioService_Constructor_InitialState()
    {
        var service = new MockAudioService(TestScenarios.BasicChat, _mockConsole.Object);

        Assert.False(service.IsRecording);
    }

    [Fact]
    public void MockAudioService_StartRecording_SetsIsRecording()
    {
        var service = new MockAudioService(TestScenarios.BasicChat, _mockConsole.Object);

        service.StartRecording();

        Assert.True(service.IsRecording);
        _mockConsole.Verify(x => x.PrintWarning(It.Is<string>(s => s.Contains("Recording started"))), Times.Once);
    }

    [Fact]
    public async Task MockAudioService_StopAndTranscribeAsync_ReturnsTranscription()
    {
        var service = new MockAudioService(TestScenarios.BasicChat, _mockConsole.Object);
        service.StartRecording();

        var transcription = await service.StopAndTranscribeAsync();

        Assert.False(service.IsRecording);
        Assert.NotNull(transcription);
        Assert.NotEmpty(transcription);
    }

    [Fact]
    public void MockAudioService_StopDiscard_StopsWithoutTranscription()
    {
        var service = new MockAudioService(TestScenarios.BasicChat, _mockConsole.Object);
        service.StartRecording();

        service.StopDiscard();

        Assert.False(service.IsRecording);
    }

    [Fact]
    public void MockAudioService_Dispose_DoesNotThrow()
    {
        var service = new MockAudioService(TestScenarios.BasicChat, _mockConsole.Object);
        service.StartRecording();

        service.Dispose();

        Assert.False(service.IsRecording);
    }

    #endregion

    #region MockDirectLlmService

    [Fact]
    public void MockDirectLlmService_Constructor_IsConfiguredTrue()
    {
        var service = new MockDirectLlmService(TestScenarios.BasicChat, _mockConsole.Object);

        Assert.True(service.IsConfigured);
    }

    [Fact]
    public async Task MockDirectLlmService_SendAsync_ReturnsResponse()
    {
        var service = new MockDirectLlmService(TestScenarios.BasicChat, _mockConsole.Object);

        var response = await service.SendAsync("Hello");

        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Theory]
    [InlineData("hello", "simulated")]
    [InlineData("help", "test mode")]
    [InlineData("who are you", "mock")]
    public async Task MockDirectLlmService_SendAsync_Keywords_ReturnsExpectedResponses(string message, string expectedContent)
    {
        var service = new MockDirectLlmService(TestScenarios.BasicChat, _mockConsole.Object);

        var response = await service.SendAsync(message);

        Assert.Contains(expectedContent, response.ToLowerInvariant());
    }

    [Fact]
    public async Task MockDirectLlmService_SendAsync_ErrorRecoveryScenario_ThrowsOnThirdCall()
    {
        var service = new MockDirectLlmService(TestScenarios.ErrorRecovery, _mockConsole.Object);

        // Test keyword path (contains "help" triggers special response, not error scenario)
        // Using messages that don't contain keywords
        var response1 = await service.SendAsync("Msg1");
        Assert.NotNull(response1);

        var response2 = await service.SendAsync("Msg2");
        Assert.NotNull(response2);

        // Third call should throw since _messageCount becomes 3
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync("Msg3"));
        Assert.Contains("Simulated LLM API error", exception.Message);
    }

    [Fact]
    public void MockDirectLlmService_Dispose_DoesNotThrow()
    {
        var service = new MockDirectLlmService(TestScenarios.BasicChat, _mockConsole.Object);

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    #endregion
}
