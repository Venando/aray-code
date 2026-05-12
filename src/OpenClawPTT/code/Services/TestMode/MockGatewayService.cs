using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services.TestMode;

/// <summary>
/// Mock implementation of IGatewayService for test mode.
/// Simulates gateway connection and returns canned responses without real network activity.
/// </summary>
public sealed class MockGatewayService : IGatewayService
{
    private readonly string _scenario;
    private readonly IColorConsole _console;
    private readonly TestScenarioSession _session;
    private bool _disposed;

    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall;
#pragma warning disable CS0067 // Event is required by IGatewayService interface but never raised in mock
    public event Action<string, JsonElement>? EventReceived;
#pragma warning restore CS0067
    public event Action<string>? AgentReplyAudio;

    public MockGatewayService(string scenario, IColorConsole console)
    {
        _scenario = scenario;
        _console = console;
        _session = new TestScenarioSession(scenario);
    }

    /// <summary>
    /// Simulates connecting to the gateway.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockGatewayService));

        _console.PrintInfo("[TEST MODE] Simulating gateway connection...");

        // Simulate connection delay
        await Task.Delay(300, ct);

        _console.PrintInfo($"[TEST MODE] Connected! Using '{_scenario}' scenario.");
        _console.PrintInfo("[TEST MODE] Type messages or use Push-to-Talk. No real network calls will be made.");
    }

    /// <summary>
    /// Simulates sending text and receiving a response.
    /// </summary>
    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockGatewayService));

        // Simulate network delay
        await Task.Delay(100, ct);

        // Check for simulated errors in error-recovery scenario
        if (_scenario == TestScenarios.ErrorRecovery && _session.MessageCount % 3 == 1 && _session.MessageCount > 0)
        {
            _console.PrintError("[TEST MODE] Simulating connection error...");
            await Task.Delay(200, ct);
            _console.PrintWarning("[TEST MODE] Error handled, continuing...");
        }

        // Simulate thinking for certain messages
        if (text.Length > 20 || text.Contains("?"))
        {
            AgentThinking?.Invoke("[Test mode: Simulating agent thinking process...]");
            await Task.Delay(300, ct);
        }

        // Simulate tool call if applicable
        var toolCall = _session.GetSimulatedToolCall(text);
        if (toolCall != null)
        {
            AgentToolCall?.Invoke(toolCall.ToolName, toolCall.Arguments);
            await Task.Delay(200, ct);
        }

        // Get and send response
        var response = _session.GetNextResponse(text);

        // Simulate streaming response
        AgentReplyDeltaStart?.Invoke();

        var words = response.Split(' ');
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            AgentReplyDelta?.Invoke(word + " ");
            await Task.Delay(30, ct); // Simulate typing speed
        }

        AgentReplyDeltaEnd?.Invoke();
        AgentReplyFull?.Invoke(response);

        // Simulate occasional audio response in multi-agent scenario
        if (_scenario == TestScenarios.MultiAgent && _session.MessageCount % 2 == 0)
        {
            await Task.Delay(100, ct);
            AgentReplyAudio?.Invoke("[Simulated audio response would play here]");
        }
    }

    /// <summary>
    /// Recreates the service with new config (no-op in test mode).
    /// </summary>
    public void RecreateWithConfig(AppConfig newConfig)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockGatewayService));
        _console.PrintInfo("[TEST MODE] Recreating gateway service (no-op)");
    }

    public Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken ct = default)
    {
        // No-op in test mode — return empty JsonElement
        return Task.FromResult(default(JsonElement));
    }

    /// <summary>
    /// Returns simulated session history.
    /// </summary>
    public Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockGatewayService));

        var history = new List<ChatHistoryEntry>
        {
            new()
            {
                Role = "assistant",
                Content = "Welcome to test mode! This is simulated history.",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new()
            {
                Role = "user",
                Content = "Can you show me some example responses?",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4)
            },
            new()
            {
                Role = "assistant",
                Content = "Sure! Here are some responses from the test scenario.",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3)
            }
        };

        return Task.FromResult<List<ChatHistoryEntry>?>(history);
    }

    /// <summary>
    /// Displays assistant reply using the console (simulated).
    /// </summary>
    public void DisplayAssistantReply(string body)
    {
        _console.PrintAgentReply("Test Agent", body);
    }

    /// <summary>
    /// Displays a history entry.
    /// </summary>
    public void DisplayHistoryEntry(ChatHistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            DisplayAssistantReply(entry.Content);
        }
    }

    /// <summary>
    /// Gets the list of mock agents for this scenario.
    /// </summary>
    public IReadOnlyList<MockAgentInfo> GetMockAgents() => _session.GetAgents();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _console.PrintInfo("[TEST MODE] Mock gateway service disposed.");
        }
    }
}
