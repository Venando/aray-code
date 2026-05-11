using System.Collections.Generic;
using StreamShell;

namespace OpenClawPTT.Tests;

/// <summary>
/// Minimal fake IInputHandler for testing save/load of input field state.
/// Tracks current input text and supports SaveInputField/LoadInputField.
/// </summary>
public sealed class FakeInputHandler : IInputHandler
{
    private string _text = string.Empty;
    private int _cursor;
    private readonly List<Attachment> _attachments = new();
    private readonly Dictionary<string, (string Text, int Cursor, List<Attachment> Attachments)> _saved = new();
    private long _saveCounter;

    public string CurrentInput => _text;
    public int CursorPosition => _cursor;
    public bool HasSelection => false;
    public bool TryGetSelection(out int start, out int length) { start = 0; length = 0; return false; }
    public int RightMargin { get; set; } = 80;
    public bool WordWrap { get; set; } = true;
    public bool QuitRequested { get; set; }
    public List<Attachment> Attachments => _attachments;
    public int LargePasteThreshold { get; set; } = 300;
    public int LargePasteLineThreshold { get; set; } = 4;

    public string? ProcessInput() => null;
    public string? ProcessInput(CancellationToken cancellationToken = default) => cancellationToken.IsCancellationRequested ? null : null;
    public void Reset()
    {
        _text = string.Empty;
        _cursor = 0;
    }

    public string SaveInputField()
    {
        string id = Interlocked.Increment(ref _saveCounter).ToString();
        var attachments = new List<Attachment>(_attachments.Count);
        foreach (var a in _attachments)
            attachments.Add(a with { });
        _saved[id] = (_text, _cursor, attachments);
        return id;
    }

    public bool LoadInputField(string id)
    {
        if (!_saved.TryGetValue(id, out var state))
            return false;

        _text = state.Text;
        _cursor = state.Cursor;
        _attachments.Clear();
        foreach (var a in state.Attachments)
            _attachments.Add(a with { });

        return true;
    }

    public bool RemoveSavedInputField(string id) => _saved.Remove(id);
    public void RemoveAllSavedInputFields() => _saved.Clear();

    public void SetInputFieldContent(string text)
    {
        _text = text ?? string.Empty;
        _cursor = _text.Length;
    }

    public IReadOnlyList<string> GetSavedInputFieldIds()
    {
        var keys = new List<string>(_saved.Count);
        foreach (var key in _saved.Keys)
            keys.Add(key);
        return keys;
    }
}
