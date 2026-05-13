using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Thin coordinator that owns child components, wires them together,
/// and delegates all operations to them.
/// </summary>
public sealed class GatewayClient : IGatewayClient
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;
    private readonly IGatewayEventSource _eventSource;
    private readonly Func<IGatewayConnectionLifecycle>? _lifecycleFactory;
    private readonly IColorConsole _console;
    private readonly IAgentStatusTracker? _agentStatusTracker;

    private IGatewayConnectionLifecycle? _lifecycle;

    private bool _isDisposed;

    /// <summary>Fires after a successful connection to the gateway (initial or reconnection).</summary>
    public event Action? ConnectionSucceeded;

    /// <summary>Fires when the reconnection loop begins after an unexpected disconnect.</summary>
    public event Action? Reconnecting;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource eventSource, IColorConsole console,
        Func<IGatewayConnectionLifecycle>? lifecycleFactory = null, IAgentStatusTracker? agentStatusTracker = null)
    {
        _cfg = cfg;
        _dev = dev;
        _eventSource = eventSource;
        _console = console;
        _lifecycleFactory = lifecycleFactory ?? (() => new GatewayConnectionLifecycle(_cfg, _dev, _eventSource, console, agentStatusTracker: agentStatusTracker));
        _lifecycle = _lifecycleFactory();
        if (_lifecycle != null)
        {
            _lifecycle.ConnectionSucceeded += () => ConnectionSucceeded?.Invoke();
            _lifecycle.Reconnecting += () => Reconnecting?.Invoke();
        }
        _agentStatusTracker = agentStatusTracker;
    }

    // ─── IGatewayClient properties ──────────────────────────────────

    public bool IsConnected => _lifecycle?.IsConnected ?? false;

    public string? SessionKey => AgentRegistry.ActiveSessionKey;

    public string? AgentId => null; // Not implemented in this version

    public bool IsDisposed => _isDisposed;

    public IGatewayEventSource? GetEventSource() => _eventSource;

    // ─── connect ────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        // lifecycle was created in constructor for testability.
        // ConnectAsync internally manages keepalive (reads tickIntervalMs from server hello).
        if (_lifecycle == null)
            throw new InvalidOperationException("Lifecycle not initialized.");
        await _lifecycle.ConnectAsync(ct);
    }

    // ─── disconnect ─────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        await (_lifecycle?.DisconnectAsync(ct) ?? Task.CompletedTask);
    }

    // ─── send ───────────────────────────────────────────────────────

    /// <summary>Send a text message via chat.send.</summary>
    public async Task<JsonElement> SendTextAsync(string body, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";
        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = body
        };

        return await TrySendAsync("chat.send", chatParams, ct);
    }

    private async Task<JsonElement> TrySendAsync(string method, object? chatParams, CancellationToken ct)
    {
        if (_lifecycle == null)
            return default;

        var framing = _lifecycle.GetFraming();

        if (framing == null)
            return default;

        return await framing.SendRequestAsync(method, chatParams, ct);
    }

    /// <summary>
    /// Send recorded WAV bytes as an audio attachment via chat.send.
    /// Returns the request ack payload.
    /// </summary>
    public async Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");

        try
        {
            await File.WriteAllBytesAsync(tempPath, wavBytes, ct);

            var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";

            var chatParams = new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
                ["idempotencyKey"] = Guid.NewGuid().ToString(),
                ["message"] = $"file://{tempPath}",
            };

            return await TrySendAsync("chat.send", chatParams, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    /// <summary>Sends a generic event/request to the gateway and returns the response payload.</summary>
    public async Task<JsonElement> SendEventAsync(string eventName, object? parameters, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        return await TrySendAsync(eventName, parameters, ct);
    }

    // ─── session history ────────────────────────────────────────────────

    /// <summary>Fetches recent chat history for a session. Returns null if unavailable.</summary>
    public async Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            return null;

        try
        {
            var messagesEl = await FetchSessionSnapshotAsync(sessionKey);
            if (messagesEl == null)
                return null;

            ExtractAgentStatusFromHistory(messagesEl.Value, sessionKey);
            return BuildChatHistoryEntries(messagesEl.Value, limit);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ─── recreate ───────────────────────────────────────────────────

    /// <summary>
    /// Recreates the client with a new config (disposes old, creates new).
    /// Currently a no-op — actual recreate is handled by GatewayService.
    /// </summary>
    public void RecreateWithConfig(AppConfig newConfig)
    {
        // No-op: GatewayClient is created and owned by GatewayService.
        // GatewayService.RecreateWithConfig disposes and recreates us.
    }

    // ─── helpers ─────────────────────────────────────────────────────

    /// <summary>Fetches the messages array from session history, trying chat.history then fallback to sessions.preview.</summary>
    private async Task<JsonElement?> FetchSessionSnapshotAsync(string sessionKey)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
        };

        // Try chat.history first (primary RPC for chat history)
        var result = await TrySendAsync("chat.history", parameters, CancellationToken.None);

        if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
        {
            // Fallback: try sessions.preview
            result = await TrySendAsync("sessions.preview", new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
            }, CancellationToken.None);

            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
                return null;
        }

        // Try different response shapes:
        // 1. chat.history returns { messages: [...] }
        // 2. sessions.preview may return { preview: [...] } or just an array
        if (result.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            return msgs;
        if (result.TryGetProperty("preview", out var prev) && prev.ValueKind == JsonValueKind.Array)
            return prev;
        if (result.ValueKind == JsonValueKind.Array)
            return result;

        return null;
    }

    /// <summary>Extracts agent status snapshots from the most recent history entries.</summary>
    private void ExtractAgentStatusFromHistory(JsonElement messages, string sessionKey)
    {
        var totalEntries = messages.GetArrayLength();
        const int extractStatusesFromHistory = 2;
        var statusExtractStartIdx = Math.Max(0, totalEntries - extractStatusesFromHistory);

        for (int i = statusExtractStartIdx; i < totalEntries; i++)
        {
            var snapshot = AgentStatusExtractor.FromHistoryMessage(messages[i], sessionKey);
            if (snapshot != null)
            {
                _agentStatusTracker?.Update(snapshot);
            }
        }
    }

    /// <summary>Projects the messages array into a list of ChatHistoryEntry, respecting the limit.</summary>
    private static List<ChatHistoryEntry> BuildChatHistoryEntries(JsonElement messages, int limit)
    {
        var entries = new List<ChatHistoryEntry>(limit);
        var totalEntries = messages.GetArrayLength();

        for (int i = totalEntries - 1; i > 0 && entries.Count < limit; i--)
        {
            if (UserMessageHelper.TryGetChatHistoryEntry(messages[i], out var entry))
            {
                entries.Add(entry!);
            }
        }

        entries.Reverse();
        return entries;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GatewayClient));
    }

    // ─── IDisposable ─────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lifecycle?.Dispose();
        _lifecycle = null;
    }
}
