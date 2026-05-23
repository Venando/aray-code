using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

/// <summary>
/// Validates Spectre.Console markup strings, returning a list of human-readable issues.
/// </summary>
public static class MarkupValidator
{
    // All decoration keywords recognised by Spectre.Console's StyleParser.
    private static readonly HashSet<string> KnownDecorations = new(StringComparer.OrdinalIgnoreCase)
    {
        "bold", "b",
        "dim",
        "italic", "i",
        "underline", "u",
        "invert",
        "conceal",
        "blink",
        "slowblink",
        "rapidblink",
        "strikethrough", "s",
        "link",          // bare [link] — URL is the content
        "default",
        "none",
    };

    // Regex patterns for colour formats accepted in markup tags.
    private static readonly Regex HexColorPattern = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex RgbColorPattern = new(@"^rgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)$", RegexOptions.Compiled);

    /// <summary>
    /// Validates <paramref name="markup"/> against Spectre.Console markup rules.
    /// </summary>
    /// <param name="markup">The markup string to validate.</param>
    /// <returns>
    /// A <see cref="MarkupValidationResult"/> that indicates whether the markup is valid
    /// and, if not, contains a list of human-readable issue descriptions.
    /// </returns>
    public static MarkupValidationResult Validate(string markup)
    {
        var issues = new List<string>();

        if (markup is null)
        {
            issues.Add("Input is null.");
            return new MarkupValidationResult(issues);
        }

        // ── Pass 1: structural / syntactic checks ──────────────────────────
        CheckStructure(markup, issues);

        // ── Pass 2: let Spectre.Console itself confirm everything is fine ──
        // This catches any edge-cases our parser misses (e.g. unknown colour
        // names, link= values with spaces, etc.) and gives us the library's
        // own error message as an extra issue when needed.
        if (issues.Count == 0)
            CheckWithLibrary(markup, issues);

        return new MarkupValidationResult(issues);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Pass 1 — structural analysis
    // ────────────────────────────────────────────────────────────────────────

    private static void CheckStructure(string markup, List<string> issues)
    {
        var tagStack = new Stack<(string Tag, int Position)>();
        int i = 0;

        while (i < markup.Length)
        {
            if (markup[i] != '[')
            {
                // Plain text — nothing to validate character by character.
                i++;
                continue;
            }

            // Peek at the next character.
            if (i + 1 < markup.Length && markup[i + 1] == '[')
            {
                // Escaped bracket "[[" — skip both characters, this is valid.
                i += 2;
                continue;
            }

            // Find the closing ']'.
            int closingBracket = markup.IndexOf(']', i + 1);
            if (closingBracket == -1)
            {
                issues.Add($"Unclosed '[' at position {i} — no matching ']' found.");
                break; // Further checks would be noise.
            }

            // Check for escaped closing bracket "]]".
            if (closingBracket + 1 < markup.Length && markup[closingBracket + 1] == ']')
            {
                // "]]" is the escape sequence for a literal ']'.  Both '[' and the
                // escaped "]]" are consumed together.  Not a markup tag — skip.
                i = closingBracket + 2;
                continue;
            }

            string tagContent = markup.Substring(i + 1, closingBracket - i - 1).Trim();

            if (tagContent.Length == 0)
            {
                issues.Add($"Empty tag '[]' at position {i}.");
                i = closingBracket + 1;
                continue;
            }

            if (tagContent == "/")
            {
                // Closing tag [/].
                if (tagStack.Count == 0)
                    issues.Add($"Unexpected closing tag '[/]' at position {i} — no matching opening tag.");
                else
                    tagStack.Pop();
            }
            else
            {
                // Opening / style tag — validate its contents.
                var tagIssues = ValidateTagContent(tagContent, i);
                issues.AddRange(tagIssues);

                if (tagIssues.Count == 0)
                    tagStack.Push((tagContent, i));
                // If the tag itself is invalid we still push a placeholder so
                // that nesting counts remain meaningful for the rest of the string.
                else
                    tagStack.Push(($"<invalid:{tagContent}>", i));
            }

            i = closingBracket + 1;
        }

        // Any tags still on the stack were never closed.
        foreach (var (tag, pos) in tagStack)
            issues.Add($"Unclosed tag '[{tag}]' opened at position {pos} — missing '[/]'.");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tag content validation
    // ────────────────────────────────────────────────────────────────────────

    private static List<string> ValidateTagContent(string tagContent, int tagPosition)
    {
        var issues = new List<string>();

        // Split on whitespace; Spectre.Console composes styles from space-
        // separated tokens, e.g. "[bold red on blue]".
        // However "link=..." must not be split on the '=' value.
        var tokens = SplitTagTokens(tagContent);

        bool foundFg = false;
        bool expectingBg = false;

        for (int t = 0; t < tokens.Count; t++)
        {
            string token = tokens[t];

            // "on" keyword introduces a background colour.
            if (token.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                if (expectingBg)
                    issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: duplicate 'on' keyword.");
                expectingBg = true;
                continue;
            }

            if (expectingBg)
            {
                if (!IsValidColor(token))
                    issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: '{token}' is not a recognised background colour (after 'on').");

                expectingBg = false;
                continue;
            }

            // link=<url>
            if (token.StartsWith("link=", StringComparison.OrdinalIgnoreCase))
            {
                string url = token.Substring("link=".Length);
                if (url.Length == 0)
                    issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: 'link=' is present but the URL is empty.");
                // Spectre.Console itself throws when the URL contains whitespace
                // (it would split into separate tokens, so this path is unreachable
                //  in practice, but guard anyway).
                continue;
            }

            // Bare "link" keyword (URL is the text content).
            if (token.Equals("link", StringComparison.OrdinalIgnoreCase))
                continue;

            // Known decoration keyword.
            if (KnownDecorations.Contains(token))
                continue;

            // Colour — named, hex, or rgb.
            if (IsValidColor(token))
            {
                if (!foundFg)
                    foundFg = true;
                else
                    issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: multiple foreground colours specified ('{token}'). Use 'on' to set a background colour.");
                continue;
            }

            // Unknown token.
            issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: '{token}' is not a recognised style, decoration, or colour. " +
                       "Check spelling or see the Spectre.Console colour/style reference.");
        }

        if (expectingBg)
            issues.Add($"Tag '[{tagContent}]' at position {tagPosition}: 'on' keyword present but no background colour follows it.");

        return issues;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a tag's inner content into tokens, keeping "link=..." intact.
    /// </summary>
    private static List<string> SplitTagTokens(string tagContent)
    {
        var tokens = new List<string>();

        foreach (string part in tagContent.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(part);
        }

        return tokens;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="token"/> is a colour that
    /// Spectre.Console will accept: a named colour, hex (#rrggbb), or rgb().
    /// </summary>
    private static bool IsValidColor(string token)
    {
        // Fast-path: hex and rgb formats are validated with regex before
        // touching the library, avoiding an exception-based round-trip.
        if (HexColorPattern.IsMatch(token)) return true;
        if (RgbColorPattern.IsMatch(token)) return true;

        // For named colours, use Style.TryParse — it is public and internally
        // runs the same colour lookup that the markup parser uses.
        // Passing the token alone (e.g. "red") is a valid minimal style string.
        return Style.TryParse(token, out _);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Pass 2 — library confirmation
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to construct a <see cref="Markup"/> object with the given text.
    /// Any exception thrown by Spectre.Console is captured as an issue.
    /// </summary>
    private static void CheckWithLibrary(string markup, List<string> issues)
    {
        try
        {
            // Constructing Markup triggers the full parse pipeline.
            _ = new Markup(markup);
        }
        catch (Exception ex)
        {
            issues.Add($"Spectre.Console parser error: {ex.Message}");
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Result type
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The result of a Spectre.Console markup validation.
/// </summary>
public sealed class MarkupValidationResult
{
    /// <summary>Whether the markup is free of detected issues.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Human-readable descriptions of every detected issue.</summary>
    public IReadOnlyList<string> Issues { get; }

    internal MarkupValidationResult(List<string> issues)
    {
        Issues = issues.AsReadOnly();
    }

    public override string ToString()
    {
        if (IsValid)
            return "Markup is valid.";

        var sb = new StringBuilder();
        sb.AppendLine($"Markup has {Issues.Count} issue(s):");
        for (int i = 0; i < Issues.Count; i++)
            sb.AppendLine($"  [{i + 1}] {Issues[i]}");
        return sb.ToString().TrimEnd();
    }
}