using System;
using System.Collections.Generic;
using System.Text;
using OpenClawPTT.Services.StatusParts;
using OpenClawPTT.Services.Themes;

namespace OpenClawPTT.Services;

/// <summary>
/// Owns the rendering pipeline for status parts: collecting parts into
/// position groups, sorting, composing Spectre-markup text, and pushing
/// the result to the <see cref="IStreamShellHost"/>.
///
/// Separated from <see cref="StatusService"/> so that service owns the
/// lifecycle/data and the renderer owns the presentation mechanics.
/// </summary>
public sealed class StatusRenderer
{
    private static ToolTheme Style => ThemeProvider.Current.Tools;

    private readonly IStreamShellHost _shellHost;

    // Reusable per-position lists to avoid allocations in Render()
    private readonly List<IStatusPart> _topLeft = new(6);
    private readonly List<IStatusPart> _topRight = new(6);
    private readonly List<IStatusPart> _bottomLeft = new(6);
    private readonly List<IStatusPart> _bottomRight = new(6);

    // Reusable StringBuilders for composing final left/right strings
    private readonly StringBuilder _sbLeft = new(256);
    private readonly StringBuilder _sbRight = new(128);

    public StatusRenderer(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
    }

    /// <summary>
    /// Composes and pushes rendered status text to the shell host for all
    /// given parts. Parts with <see cref="DisplayPosition.None"/> are skipped.
    /// Non-dirty parts use cached text; dirty ones are rebuilt.
    /// </summary>
    public void Render(IStatusPart[] allParts)
    {
        try
        {
            // Collect parts into position groups
            _topLeft.Clear();
            _topRight.Clear();
            _bottomLeft.Clear();
            _bottomRight.Clear();

            foreach (var part in allParts)
            {
                switch (part.Position)
                {
                    case DisplayPosition.TopSeparatorLeft:
                        _topLeft.Add(part);
                        break;
                    case DisplayPosition.TopSeparatorRight:
                        _topRight.Add(part);
                        break;
                    case DisplayPosition.BottomSeparatorLeft:
                        _bottomLeft.Add(part);
                        break;
                    case DisplayPosition.BottomSeparatorRight:
                        _bottomRight.Add(part);
                        break;
                    // AppStatusPanelLeft / AppStatusPanelRight are rendered
                    // exclusively by AppStatusBottomPanel — never on the separator.
                    // DisplayPosition.None: skip entirely
                }
            }

            // Sort each group by Order
            SortByOrder(_topLeft);
            SortByOrder(_topRight);
            SortByOrder(_bottomLeft);
            SortByOrder(_bottomRight);

            // Build and set top separator
            string topLeftText = BuildTopLeftText(ComposePositionText(_topLeft));
            string topRightText = ComposePositionText(_topRight);
            _shellHost.SetTopSeparator(leftText: topLeftText, rightText: topRightText,
                repeatedCharacter: Style.SeparatorChar[0], repeatedCharMarkup: Style.SeparatorCharMarkup);

            // Build and set bottom separator if any parts are assigned to it
            if (_bottomLeft.Count > 0 || _bottomRight.Count > 0)
            {
                string bottomLeftText = ComposePositionText(_bottomLeft);
                string bottomRightText = ComposePositionText(_bottomRight);
                _shellHost.SetBottomSeparator(leftText: bottomLeftText, rightText: bottomRightText,
                    repeatedCharacter: Style.SeparatorChar[0], repeatedCharMarkup: Style.SeparatorCharMarkup);
            }
        }
        catch (Exception ex)
        {
            // Rendering is best-effort — never crash the caller if shell is disposed
            System.Diagnostics.Debug.WriteLine($"StatusRenderer failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks all parts clean after their rendered text has been consumed.
    /// </summary>
    public static void MarkAllClean(IStatusPart[] allParts)
    {
        foreach (var part in allParts)
        {
            if (part.IsDirty)
                part.MarkClean();
        }
    }

    /// <summary>
    /// Builds the top separator left-side text by prepending the separator
    /// line prefix. When the left side is empty, returns empty string.
    /// </summary>
    private string BuildTopLeftText(string partsText)
    {
        if (string.IsNullOrEmpty(partsText))
            return string.Empty;

        _sbLeft.Clear();
        _sbLeft.Append(Style.TopLeftSeparatorPrefix);
        _sbLeft.Append(partsText);
        _sbLeft.Append(' ');
        return _sbLeft.ToString();
    }

    /// <summary>
    /// Composes the text for a list of parts by concatenating their
    /// rendered values with appropriate separators.  Uses cached text
    /// for non-dirty parts, rebuilding only dirty ones.
    /// </summary>
    private string ComposePositionText(List<IStatusPart> parts)
    {
        if (parts.Count == 0)
            return string.Empty;

        _sbRight.Clear();
        bool first = true;

        foreach (var part in parts)
        {
            string text = part.GetText();
            if (string.IsNullOrEmpty(text))
                continue;

            if (!first)
            {
                _sbRight.Append(part.SeparatorBefore);
            }

            _sbRight.Append(text);
            first = false;
        }

        return _sbRight.ToString();
    }

    /// <summary>Sorts a list of parts in-place by <see cref="IStatusPart.Order"/>.</summary>
    private static void SortByOrder(List<IStatusPart> parts)
    {
        if (parts.Count > 1)
            parts.Sort((a, b) => a.Order.CompareTo(b.Order));
    }
}
