using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// A minimal bottom panel that displays nothing.
/// Used to suppress the default panel during wizards like /crew config and /reconfigure.
/// </summary>
public sealed class EmptyBottomPanel : IBottomPanel
{
    private static readonly string[] EmptyLines = [""];

    public int LineCount => 1;
    public bool IsDirty => false;
    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => false;
    public bool AllowUserField => true;

    public IReadOnlyList<string> GetLines(string currentInput) => EmptyLines;

    public void ClearDirty() { }

    public bool TryHandleKey(ConsoleKeyInfo key) => false;

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { }
}
