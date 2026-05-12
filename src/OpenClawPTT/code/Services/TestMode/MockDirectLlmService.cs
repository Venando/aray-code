using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services.TestMode;

/// <summary>
/// Mock implementation of IDirectLlmService for test mode.
/// Returns predefined responses without making real LLM API calls.
/// </summary>
public sealed class MockDirectLlmService : IDirectLlmService
{
    private readonly string _scenario;
    private readonly IColorConsole _console;
    private readonly TestScenarioSession _session;
    private bool _disposed;
    private int _messageCount;

    /// <summary>
    /// Always returns true in test mode.
    /// </summary>
    public bool IsConfigured => true;

    public MockDirectLlmService(string scenario, IColorConsole console)
    {
        _scenario = scenario;
        _console = console;
        _session = new TestScenarioSession(scenario);
    }

    /// <summary>
    /// Simulates sending a message to an LLM and returns a canned response.
    /// </summary>
    public async Task<string> SendAsync(string message, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockDirectLlmService));

        _console.PrintInfo($"[TEST MODE] Sending to mock LLM: \"{message}\"");

        // Simulate network/API delay
        await Task.Delay(500, ct);

        _messageCount++;

        // Generate response based on scenario and message content
        var response = GenerateResponse(message);

        _console.PrintInfo("[TEST MODE] Mock LLM response received.");

        return response;
    }

    /// <summary>
    /// Always returns true in test mode — simulates a reachable endpoint.
    /// </summary>
    public Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockDirectLlmService));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Generates a response based on the input message and scenario.
    /// </summary>
    private string GenerateResponse(string message)
    {
        var lowerMessage = message.ToLowerInvariant();

        // Check for specific keywords first
        if (lowerMessage.Contains("hello") || lowerMessage.Contains("hi"))
        {
            return "Hello! I'm a simulated LLM response in test mode. No actual AI was used to generate this message.";
        }

        if (lowerMessage.Contains("help"))
        {
            return "This is test mode for OpenClaw PTT. Available commands:\n" +
                   "- Say 'error' to test error handling\n" +
                   "- Say 'tool' to simulate a tool call\n" +
                   "- Say 'agent' to test agent switching\n" +
                   "- Any other message gets a canned response.";
        }

        if (lowerMessage.Contains("time") || lowerMessage.Contains("date"))
        {
            return $"The current time is {DateTime.Now:HH:mm:ss} (simulated response from test mode).";
        }

        if (lowerMessage.Contains("who are you") || lowerMessage.Contains("what are you"))
        {
            return "I am a mock LLM service for testing OpenClaw PTT. I'm not a real AI - I provide predefined responses for development and testing purposes.";
        }

        // Return scenario-specific response
        return _scenario switch
        {
            TestScenarios.ErrorRecovery => GetErrorRecoveryResponse(message),
            TestScenarios.MultiAgent => GetMultiAgentResponse(message),
            _ => GetBasicChatResponse(message)
        };
    }

    private string GetBasicChatResponse(string message)
    {
        var responses = new[]
        {
            "This is a mock response from the Direct LLM service in test mode.",
            "In test mode, all LLM calls are simulated with canned responses like this one.",
            "The real DirectLlmService would connect to OpenAI, Anthropic, or another LLM provider.",
            "Test mode allows you to develop without API keys or internet connectivity.",
            "You can test message formatting, streaming, and error handling in isolation."
        };

        return responses[_session.MessageCount % responses.Length];
    }

    private string GetErrorRecoveryResponse(string message)
    {
        // Every third message simulates an error (counts: 3, 6, 9, etc.)
        if (_messageCount % 3 == 0)
        {
            throw new InvalidOperationException("[TEST MODE] Simulated LLM API error. This tests error handling in the Direct LLM feature.");
        }

        return "ErrorRecovery scenario: This response succeeded. The next one might fail to test error handling.";
    }

    private string GetMultiAgentResponse(string message)
    {
        var agents = new[] { "Alpha", "Beta", "Gamma" };
        var agent = agents[_messageCount % agents.Length];

        return $"[Agent {agent} - Direct LLM] This response comes from the simulated {agent} agent in multi-agent test mode.";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
