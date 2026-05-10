using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services.Diagnostics;

namespace OpenClawPTT.Services;

public sealed class GatewayService : IGatewayService
{
    private readonly AppConfig _config;
    private readonly IColorConsole _console;
    private readonly ITtsSummarizer? _summarizer;
    private readonly IPttStateMachine? _pttStateMachine;
    private readonly IAgentStatusTracker? _agentStatusTracker;
    private readonly DeviceIdentity _device;
    private IGatewayClient _gatewayClient;
    private AgentOutputCoordinator _coordinator;
    private ErrorLogStore? _errorLog;
    private bool _disposed;

    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall; // (toolName, arguments)
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string>? AgentReplyAudio;

    public GatewayService(AppConfig config, IColorConsole console, AgentOutputCoordinator coordinator, ITtsSummarizer? summarizer = null, IPttStateMachine? pttStateMachine = null, IAgentStatusTracker? agentStatusTracker = null)
    {
        _config = config;
        _console = console;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _summarizer = summarizer;
        _pttStateMachine = pttStateMachine;
        _agentStatusTracker = agentStatusTracker;
        _device = new DeviceIdentity(config.DataDir);
        _device.EnsureKeypair();
        _gatewayClient = CreateGatewayClient();
    }

    /// <summary>Wire an ErrorLogStore for logging send/connect failures.</summary>
    public void SetErrorLogStore(ErrorLogStore store)
    {
        _errorLog = store;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gatewayClient.ConnectAsync(ct);
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        await _gatewayClient.SendTextAsync(text, ct);
    }

    public async Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken ct)
    {
        try
        {
            return await _gatewayClient.SendEventAsync(method, parameters, ct);
        }
        catch (GatewayException ex)
        {
            LogClassifiedError(GatewayErrorClassifier.ClassifyGatewayError(ex), ex);
            throw; // Re-throw so callers can handle failure UI
        }
        catch (Exception ex)
        {
            LogClassifiedError(GatewayErrorClassifier.Classify(ex), ex);
            throw; // Re-throw so callers can handle failure UI
        }
    }

    private void LogClassifiedError(ErrorClassification classification, Exception ex)
    {
        _errorLog?.Write(classification.ToLogEntry());
    }

    public void RecreateWithConfig(AppConfig newConfig)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GatewayService));

        _gatewayClient.Dispose();
        _gatewayClient = CreateGatewayClient();
    }

    public async Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        return await _gatewayClient.FetchSessionHistoryAsync(sessionKey, limit);
    }

    public void DisplayAssistantReply(string body)
    {
        _coordinator.OnAgentReplyFull(body);
    }

    public void DisplayHistoryEntry(ChatHistoryEntry entry)
    {
        // Render thinking via ThinkingDisplayHandler (respects ThinkingDisplayMode config)
        if (!string.IsNullOrWhiteSpace(entry.Thinking))
        {
            var thinkingHandler = new ThinkingDisplayHandler(_config, _console.GetStreamShellHost());
            thinkingHandler.DisplayThinking(entry.Thinking);
        }

        // Render tool calls via ToolDisplayHandler if any
        if (entry.ToolCalls.Count > 0)
        {
            var toolHandler = new ToolDisplayHandler(_config.ReservedRightMargin, _console.GetStreamShellHost());
            foreach (var toolCall in entry.ToolCalls)
            {
                if (!string.IsNullOrEmpty(toolCall.ToolName))
                    toolHandler.Handle(toolCall.ToolName, toolCall.Arguments);
            }
        }

        // Render the reply text
        if (!string.IsNullOrWhiteSpace(entry.Content))
            DisplayAssistantReply(entry.Content);
    }

    private IGatewayClient CreateGatewayClient()
    {
        var client = new GatewayClient(_config, _device, new GatewayEventSource(), _console, agentStatusTracker: _agentStatusTracker);
        var events = ((IGatewayClient)client).GetEventSource();

        if (events != null)
            WireEventHandlers(events);

        return client;
    }

    /// <summary>
    /// Wires all event handlers on the gateway event source.
    /// Some events depend on the display mode (delta vs full reply),
    /// while others (thinking, tool calls, audio, received) are unconditional.
    /// Extracted from CreateGatewayClient for SRP.
    /// </summary>
    private void WireEventHandlers(IGatewayEventSource events)
    {
        bool useDelta = _config.ReplyDisplayMode != ReplyDisplayMode.Full;
        bool useFull = _config.ReplyDisplayMode != ReplyDisplayMode.Delta;

        // ── Always wired (display-mode independent) ──
        events.AgentThinking += thinking =>
        {
            _coordinator.OnAgentThinking(thinking);
            AgentThinking?.Invoke(thinking);
        };

        events.AgentToolCall += (toolName, arguments) =>
        {
            _coordinator.OnAgentToolCall(toolName, arguments);
            AgentToolCall?.Invoke(toolName, arguments);
        };

        events.AgentReplyAudio += audioText =>
        {
            _coordinator.OnAgentReplyAudio(audioText);
            AgentReplyAudio?.Invoke(audioText);
        };

        events.EventReceived += (name, json) =>
        {
            EventReceived?.Invoke(name, json);
        };

        // ── Delta path (display mode: streaming) ──
        if (useDelta)
        {
            events.AgentReplyDeltaStart += () =>
            {
                _coordinator.OnAgentReplyDeltaStart();
                AgentReplyDeltaStart?.Invoke();
            };

            events.AgentReplyDelta += delta =>
            {
                _coordinator.OnAgentReplyDelta(delta);
                AgentReplyDelta?.Invoke(delta);
            };

            events.AgentReplyDeltaEnd += () =>
            {
                _coordinator.OnAgentReplyDeltaEnd();
                AgentReplyDeltaEnd?.Invoke();
            };
        }

        // ── Full reply path (display mode: batched) ──
        if (useFull)
        {
            events.AgentReplyFull += body =>
            {
                _coordinator.OnAgentReplyFull(body);
                AgentReplyFull?.Invoke(body);
            };
        }
    }



    public void Dispose()
    {
        if (!_disposed)
        {
            _coordinator.Dispose();
            _gatewayClient.Dispose();
            _disposed = true;
        }
    }
}
