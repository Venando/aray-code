using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace OpenClawPTT.Services.Diagnostics;

/// <summary>A single error log entry.</summary>
public sealed class ErrorLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "error";
    public string Category { get; init; } = "gateway";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string[] SuggestedActions { get; init; } = Array.Empty<string>();
    public int? RetryAttempt { get; init; }
    public string? RawException { get; init; }
    public string? StackTrace { get; init; }
}

/// <summary>
/// Thread-safe, append-only error log store.
/// Persists to {dataDir}/diagnostics/errors.json with bounded capacity.
/// </summary>
public sealed class ErrorLogStore
{
    private const int DefaultMaxEntries = 1000;
    private readonly string _filePath;
    private readonly int _maxEntries;
    private readonly ReaderWriterLockSlim _lock = new();

    private List<ErrorLogEntry> _entries = new();

    public ErrorLogStore(string dataDir, int maxEntries = DefaultMaxEntries)
    {
        var dir = Path.Combine(dataDir, "diagnostics");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "errors.json");
        _maxEntries = maxEntries;
        _entries = LoadFromDisk();
    }

    /// <summary>Append an error entry. Thread-safe. Auto-trims oldest when full.</summary>
    public void Write(ErrorLogEntry entry)
    {
        _lock.EnterWriteLock();
        try
        {
            _entries.Add(entry);
            if (_entries.Count > _maxEntries)
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
            SaveToDisk();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Get the most recent N entries. Thread-safe.</summary>
    public IReadOnlyList<ErrorLogEntry> GetRecent(int count = 50)
    {
        _lock.EnterReadLock();
        try
        {
            if (_entries.Count <= count)
                return _entries.ToArray();
            return _entries.GetRange(_entries.Count - count, count).ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Get all entries (for display). Thread-safe.</summary>
    public IReadOnlyList<ErrorLogEntry> GetAll()
    {
        _lock.EnterReadLock();
        try
        {
            return _entries.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Clear all entries. Thread-safe.</summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _entries.Clear();
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Total entries stored.</summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _entries.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    // ─── persistence ──────────────────────────────────────────────

    private List<ErrorLogEntry> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<ErrorLogEntry>();

            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<ErrorLogEntry>>(json);
            return entries ?? new List<ErrorLogEntry>();
        }
        catch
        {
            return new List<ErrorLogEntry>();
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort — don't crash if we can't write
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
