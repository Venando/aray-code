using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ArayCode.Services.TestMode;

/// <summary>
/// Defines test scenarios for the test mode feature.
/// Each scenario provides canned responses and behavior patterns for testing without real services.
/// </summary>
public static class TestScenarios
{
    public const string BasicChat = "basic-chat";
    public const string ErrorRecovery = "error-recovery";
    public const string MultiAgent = "multi-agent";

    /// <summary>
    /// Gets the list of available scenario names.
    /// </summary>
    public static IReadOnlyList<string> AvailableScenarios => new[] { BasicChat, ErrorRecovery, MultiAgent };

    /// <summary>
    /// Gets a human-readable description for a scenario.
    /// </summary>
    public static string GetDescription(string scenario) => scenario switch
    {
        BasicChat => "Simple back-and-forth chat with canned responses",
        ErrorRecovery => "Simulates errors and recovery scenarios",
        MultiAgent => "Simulates multiple agents responding in sequence",
        _ => "Unknown scenario"
    };

    /// <summary>
    /// Validates if a scenario name is valid.
    /// </summary>
    public static bool IsValid(string? scenario) =>
        !string.IsNullOrWhiteSpace(scenario) && AvailableScenarios.Contains(scenario);

    /// <summary>
    /// Gets the default scenario name.
    /// </summary>
    public static string Default => BasicChat;
}

/// <summary>
/// Holds the state and response queue for a test scenario session.
/// </summary>
public class TestScenarioSession
{
    private readonly string _scenario;
    private readonly Queue<string> _responseQueue;
    private int _messageCount;
    private readonly Random _random = new();

    public TestScenarioSession(string scenario)
    {
        _scenario = scenario;
        _responseQueue = new Queue<string>();
        InitializeResponses();
    }

    public string Scenario => _scenario;
    public int MessageCount => _messageCount;

    private void InitializeResponses()
    {
        switch (_scenario)
        {
            case TestScenarios.BasicChat:
                EnqueueBasicChatResponses();
                break;
            case TestScenarios.ErrorRecovery:
                EnqueueErrorRecoveryResponses();
                break;
            case TestScenarios.MultiAgent:
                EnqueueMultiAgentResponses();
                break;
        }
    }

    private void EnqueueBasicChatResponses()
    {
        var responses = new[]
        {
            "Hello! I'm a test agent. How can I help you today?",
            "That's an interesting question. In test mode, I provide predefined responses to simulate conversation.",
            "I can simulate tool calls, streaming responses, and more. Try sending a few messages!",
            "This is a canned response from the basic-chat scenario. The real agent would use AI to generate this.",
            "Test mode allows you to develop and debug the UI without needing a real gateway connection.",
            "You can test hotkeys, message formatting, and the push-to-talk functionality in isolation."
        };
        foreach (var r in responses) _responseQueue.Enqueue(r);
    }

    private void EnqueueErrorRecoveryResponses()
    {
        var responses = new[]
        {
            "ErrorRecovery scenario: First message succeeds.",
            "[ERROR: Simulated connection failure] This tests how the UI handles errors.",
            "Recovery complete. The system should have shown an error and continued.",
            "[TIMEOUT: Simulated slow response] This message simulates a delay.",
            "Normal service resumed after simulated timeout.",
            "Try sending 'error' to trigger a simulated failure on demand."
        };
        foreach (var r in responses) _responseQueue.Enqueue(r);
    }

    private void EnqueueMultiAgentResponses()
    {
        var responses = new[]
        {
            "[Agent Alpha] I'm the first agent in this multi-agent scenario.",
            "[Agent Beta] And I'm the second agent. We alternate responses.",
            "[Agent Alpha] You can use this scenario to test agent switching UI.",
            "[Agent Beta] Each response simulates a different agent speaking.",
            "[Agent Gamma] I'm a third agent that occasionally chimes in!",
            "[Agent Alpha] Try using /switch or agent commands to change between us."
        };
        foreach (var r in responses) _responseQueue.Enqueue(r);
    }

    /// <summary>
    /// Gets the next response for the current scenario.
    /// </summary>
    public string GetNextResponse(string? userMessage = null)
    {
        _messageCount++;

        // Check for special trigger words
        if (userMessage != null)
        {
            var lowerMsg = userMessage.ToLowerInvariant();
            if (lowerMsg.Contains("error"))
                return "[SIMULATED ERROR] You triggered an error response!";
            if (lowerMsg.Contains("timeout"))
                return "[SIMULATED TIMEOUT] This response simulates a network timeout.";
            if (lowerMsg.Contains("agent"))
                return "[Agent Switch Test] Use /switch or Ctrl+Tab to change agents.";
        }

        // Return from queue or cycle
        if (_responseQueue.Count > 0)
            return _responseQueue.Dequeue();

        // Generate a generic response when queue is empty
        return $"[Test Mode Message #{_messageCount}] This is a generated response for the {_scenario} scenario.";
    }

    /// <summary>
    /// Simulates a streaming response by yielding chunks.
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingResponse(string? userMessage = null)
    {
        var fullResponse = GetNextResponse(userMessage);
        var words = fullResponse.Split(' ');

        foreach (var word in words)
        {
            yield return word + " ";
            await Task.Delay(_random.Next(10, 50)); // Simulate typing delay
        }
    }

    /// <summary>
    /// Gets a list of simulated agents for this scenario.
    /// </summary>
    public IReadOnlyList<MockAgentInfo> GetAgents() => _scenario switch
    {
        TestScenarios.MultiAgent => new[]
        {
            new MockAgentInfo("alpha", "Agent Alpha", "Primary assistant for testing"),
            new MockAgentInfo("beta", "Agent Beta", "Secondary assistant for testing"),
            new MockAgentInfo("gamma", "Agent Gamma", "Tertiary assistant for testing")
        },
        TestScenarios.ErrorRecovery => new[]
        {
            new MockAgentInfo("test", "Test Agent", "Agent with simulated error capabilities"),
            new MockAgentInfo("stable", "Stable Agent", "Reliable agent for comparison")
        },
        _ => new[]
        {
            new MockAgentInfo("default", "Test Agent", "Default test mode agent")
        }
    };

    /// <summary>
    /// Simulates a tool call for scenarios that support it.
    /// </summary>
    public MockToolCall? GetSimulatedToolCall(string? userMessage = null)
    {
        if (_scenario == TestScenarios.ErrorRecovery && _messageCount % 3 == 0)
        {
            return new MockToolCall("test_tool", "{\"action\": \"simulate_error\", \"code\": 500}");
        }

        if (userMessage?.Contains("tool") == true || userMessage?.Contains("function") == true)
        {
            return new MockToolCall("search", "{\"query\": \"test query\", \"results\": []}");
        }

        return null;
    }
}

/// <summary>
/// Information about a mock agent for test mode.
/// </summary>
public record MockAgentInfo(string Id, string Name, string Description);

/// <summary>
/// Represents a simulated tool call.
/// </summary>
public record MockToolCall(string ToolName, string Arguments);
