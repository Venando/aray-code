using ArayCode.Services.TestMode;
using Xunit;

namespace ArayCode.Tests.Services.TestMode;

public class TestScenariosTests
{
    #region AvailableScenarios

    [Fact]
    public void AvailableScenarios_ContainsExpectedScenarios()
    {
        var scenarios = TestScenarios.AvailableScenarios;

        Assert.Contains(TestScenarios.BasicChat, scenarios);
        Assert.Contains(TestScenarios.ErrorRecovery, scenarios);
        Assert.Contains(TestScenarios.MultiAgent, scenarios);
        Assert.Equal(3, scenarios.Count);
    }

    #endregion

    #region GetDescription

    [Theory]
    [InlineData(TestScenarios.BasicChat, "Simple back-and-forth chat with canned responses")]
    [InlineData(TestScenarios.ErrorRecovery, "Simulates errors and recovery scenarios")]
    [InlineData(TestScenarios.MultiAgent, "Simulates multiple agents responding in sequence")]
    [InlineData("unknown", "Unknown scenario")]
    public void GetDescription_ReturnsExpectedDescription(string scenario, string expected)
    {
        var description = TestScenarios.GetDescription(scenario);

        Assert.Equal(expected, description);
    }

    #endregion

    #region IsValid

    [Theory]
    [InlineData(TestScenarios.BasicChat, true)]
    [InlineData(TestScenarios.ErrorRecovery, true)]
    [InlineData(TestScenarios.MultiAgent, true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpectedResult(string? scenario, bool expected)
    {
        var result = TestScenarios.IsValid(scenario);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Default

    [Fact]
    public void Default_ReturnsBasicChat()
    {
        Assert.Equal(TestScenarios.BasicChat, TestScenarios.Default);
    }

    #endregion

    #region TestScenarioSession

    [Theory]
    [InlineData(TestScenarios.BasicChat)]
    [InlineData(TestScenarios.ErrorRecovery)]
    [InlineData(TestScenarios.MultiAgent)]
    public void TestScenarioSession_Constructor_SetsScenario(string scenario)
    {
        var session = new TestScenarioSession(scenario);

        Assert.Equal(scenario, session.Scenario);
    }

    [Fact]
    public void TestScenarioSession_GetNextResponse_IncrementsMessageCount()
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);

        Assert.Equal(0, session.MessageCount);

        session.GetNextResponse();

        Assert.Equal(1, session.MessageCount);
    }

    [Fact]
    public void TestScenarioSession_GetNextResponse_BasicChat_ReturnsResponses()
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);

        var response = session.GetNextResponse();

        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Theory]
    [InlineData("error", "[SIMULATED ERROR]")]
    [InlineData("timeout", "[SIMULATED TIMEOUT]")]
    [InlineData("agent", "[Agent Switch Test]")]
    public void TestScenarioSession_GetNextResponse_TriggerWords_ReturnsSpecialResponses(string trigger, string expectedPrefix)
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);

        var response = session.GetNextResponse($"test {trigger} message");

        Assert.StartsWith(expectedPrefix, response);
    }

    [Fact]
    public void TestScenarioSession_GetNextResponse_WhenQueueEmpty_ReturnsGenericResponse()
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);

        // Exhaust the queue
        for (int i = 0; i < 10; i++)
        {
            session.GetNextResponse();
        }

        var response = session.GetNextResponse();

        Assert.Contains("Test Mode Message", response);
    }

    [Theory]
    [InlineData(TestScenarios.MultiAgent, 3)]
    [InlineData(TestScenarios.ErrorRecovery, 2)]
    [InlineData(TestScenarios.BasicChat, 1)]
    public void TestScenarioSession_GetAgents_ReturnsExpectedCount(string scenario, int expectedCount)
    {
        var session = new TestScenarioSession(scenario);
        var agents = session.GetAgents();

        Assert.Equal(expectedCount, agents.Count);
    }

    [Fact]
    public void TestScenarioSession_GetSimulatedToolCall_TriggerWord_ReturnsToolCall()
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);

        var toolCall = session.GetSimulatedToolCall("use tool please");

        Assert.NotNull(toolCall);
        Assert.Equal("search", toolCall.ToolName);
    }

    [Fact]
    public async Task TestScenarioSession_GetStreamingResponse_YieldsChunks()
    {
        var session = new TestScenarioSession(TestScenarios.BasicChat);
        var chunks = new List<string>();

        await foreach (var chunk in session.GetStreamingResponse())
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks.Count > 0);
    }

    #endregion
}
