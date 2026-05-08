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
    private readonly DeviceIdentity _device;
    private IGatewayClient _gatewayClient;
    private AgentOutputAdapter? _uiAdapter;
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

    public GatewayService(AppConfig config, IColorConsole console, ITtsSummarizer? summarizer = null, IPttStateMachine? pttStateMachine = null)
    {
        _config = config;
        _console = console;
        _summarizer = summarizer;
        _pttStateMachine = pttStateMachine;
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

        // Proactively check provider quotas after connection
        // Delayed slightly to let the initial session setup complete
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, ct);
                await CheckUsageStatusAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _console.Log("debug", $"usage.status check failed: {ex.Message}", LogLevel.Debug);
            }
        }, ct);
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        try
        {
            await _gatewayClient.SendTextAsync(text, ct);

            // After a successful send, run background checks to detect fallback
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200, ct);
                    // Check provider quota (covers exhausted primary models)
                    await CheckUsageStatusAsync(ct);
                    // Check session model override (detects silent fallback)
                    await CheckSessionModelOverrideAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _console.Log("debug", $"Post-send checks failed: {ex.Message}", LogLevel.Debug);
                }
            }, ct);
        }
        catch
        {
            throw;
        }
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

        _uiAdapter?.Dispose();
        _gatewayClient.Dispose();
        _gatewayClient = CreateGatewayClient();
    }

    public async Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        return await _gatewayClient.FetchSessionHistoryAsync(sessionKey, limit);
    }

    public void DisplayAssistantReply(string body)
    {
        _uiAdapter?.OnAgentReplyFull(body);
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
            var toolHandler = new ToolDisplayHandler(_config.RightMarginIndent, _console.GetStreamShellHost());
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
        _uiAdapter = new AgentOutputAdapter(_config, _console, _summarizer, _pttStateMachine);
        var client = new GatewayClient(_config, _device, new GatewayEventSource(), _console);
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
            _uiAdapter!.OnAgentThinking(thinking);
            AgentThinking?.Invoke(thinking);
        };

        events.AgentToolCall += (toolName, arguments) =>
        {
            _uiAdapter!.OnAgentToolCall(toolName, arguments);
            AgentToolCall?.Invoke(toolName, arguments);
        };

        events.AgentReplyAudio += audioText =>
        {
            _uiAdapter!.OnAgentReplyAudio(audioText);
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
                _uiAdapter!.OnAgentReplyDeltaStart();
                AgentReplyDeltaStart?.Invoke();
            };

            events.AgentReplyDelta += delta =>
            {
                _uiAdapter!.OnAgentReplyDelta(delta);
                AgentReplyDelta?.Invoke(delta);
            };

            events.AgentReplyDeltaEnd += () =>
            {
                _uiAdapter!.OnAgentReplyDeltaEnd();
                AgentReplyDeltaEnd?.Invoke();
            };
        }

        // ── Full reply path (display mode: batched) ──
        if (useFull)
        {
            events.AgentReplyFull += body =>
            {
                _uiAdapter!.OnAgentReplyFull(body);
                AgentReplyFull?.Invoke(body);
            };
        }
    }

    /// <summary>
    /// Calls the usage.status RPC to check provider quota status.
    /// If any provider has exhausted its quota, shows a warning.
    /// </summary>
    private async Task CheckUsageStatusAsync(CancellationToken ct)
    {
        try
        {
            var result = await _gatewayClient.SendEventAsync("usage.status", null, ct);
            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
                return;

            _console.Log("debug", $"usage.status response: {result.ToString()[..Math.Min(result.ToString().Length, 500)]}", LogLevel.Debug);

            // Parse response looking for exhausted/cooldown providers
            var providers = result.ValueKind == JsonValueKind.Object
                ? result.EnumerateObject()
                : default;

            foreach (var prop in providers)
            {
                var providerName = prop.Name;
                var status = prop.Value;

                // Check for quota exhaustion indicators
                var statusStr = status.ToString();
                if (statusStr.Contains("exhausted", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("cooldown", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
                    statusStr.Contains("billing", StringComparison.OrdinalIgnoreCase))
                {
                    _console.PrintModelQuotaWarning(providerName, statusStr[..Math.Min(statusStr.Length, 300)]);
                }
            }
        }
        catch (GatewayException gex)
        {
            // usage.status might not be available if scope is insufficient
            _console.Log("debug", $"usage.status RPC: {gex.Message}", LogLevel.Debug);
        }
    }

    /// <summary>
    /// Checks the current session model state to detect if a fallback
    /// auto-override was applied. When modelOverrideSource is "auto",
    /// the primary model failed and a fallback was used instead.
    /// </summary>
    private async Task CheckSessionModelOverrideAsync(CancellationToken ct)
    {
        try
        {
            var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";
            var result = await _gatewayClient.SendEventAsync("sessions.list", new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey
            }, ct);

            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
                return;

            _console.Log("debug", $"sessions.list response: {result.ToString()[..Math.Min(result.ToString().Length, 800)]}", LogLevel.Debug);

            // Try to find the session and check modelOverrideSource
            JsonElement sessionEl = result;
            // The response might be an array or have a sessions property
            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in result.EnumerateArray())
                {
                    if (s.TryGetProperty("key", out var k) && k.GetString() == sessionKey)
                    {
                        sessionEl = s;
                        break;
                    }
                }
            }
            else if (result.TryGetProperty("sessions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in arr.EnumerateArray())
                {
                    if (s.TryGetProperty("key", out var k) && k.GetString() == sessionKey)
                    {
                        sessionEl = s;
                        break;
                    }
                }
            }

            // Check for modelOverrideSource = "auto" or model/provider override fields
            if (sessionEl.TryGetProperty("modelOverrideSource", out var source) &&
                source.GetString() == "auto")
            {
                var currentModel = sessionEl.TryGetProperty("model", out var m) ? m.GetString() : "unknown";
                var currentProvider = sessionEl.TryGetProperty("modelProvider", out var p) ? p.GetString() : "unknown";
                var originalModel = sessionEl.TryGetProperty("originalModel", out var om) ? om.GetString() : null;
                var originalProvider = sessionEl.TryGetProperty("originalProvider", out var op) ? op.GetString() : null;

                _console.PrintModelFallback(
                    originalProvider ?? currentProvider ?? "?",
                    originalModel ?? currentModel ?? "?",
                    currentProvider ?? "?",
                    currentModel ?? "?",
                    false);
            }
        }
        catch (GatewayException gex)
        {
            _console.Log("debug", $"sessions.list check: {gex.Message}", LogLevel.Debug);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _uiAdapter?.Dispose();
            _gatewayClient.Dispose();
            _disposed = true;
        }
    }
}
