using System.Text.RegularExpressions;
// ─────────────────────────────────────────────────────────────────────────────
//  Parser
// ─────────────────────────────────────────────────────────────────────────────

public static class TerminalCommandParser
{
    // Executables → ShellCommandType lookup
    private static readonly Dictionary<string, ShellCommandType> ExecutableMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // FileSystem
            ["rm"] = ShellCommandType.FileSystem,
            ["cp"] = ShellCommandType.FileSystem,
            ["mv"] = ShellCommandType.FileSystem,
            ["mkdir"] = ShellCommandType.FileSystem,
            ["rmdir"] = ShellCommandType.FileSystem,
            ["ls"] = ShellCommandType.FileSystem,
            ["cd"] = ShellCommandType.FileSystem,
            ["chmod"] = ShellCommandType.FileSystem,
            ["chown"] = ShellCommandType.FileSystem,
            ["find"] = ShellCommandType.FileSystem,
            ["touch"] = ShellCommandType.FileSystem,
            ["ln"] = ShellCommandType.FileSystem,
            // FileContent
            ["cat"] = ShellCommandType.FileContent,
            ["echo"] = ShellCommandType.FileContent,
            ["grep"] = ShellCommandType.FileContent,
            ["sed"] = ShellCommandType.FileContent,
            ["awk"] = ShellCommandType.FileContent,
            ["head"] = ShellCommandType.FileContent,
            ["tail"] = ShellCommandType.FileContent,
            ["tee"] = ShellCommandType.FileContent,
            ["diff"] = ShellCommandType.FileContent,
            ["wc"] = ShellCommandType.FileContent,
            ["sort"] = ShellCommandType.FileContent,
            ["uniq"] = ShellCommandType.FileContent,
            ["cut"] = ShellCommandType.FileContent,
            ["tr"] = ShellCommandType.FileContent,
            // Process
            ["kill"] = ShellCommandType.Process,
            ["ps"] = ShellCommandType.Process,
            ["top"] = ShellCommandType.Process,
            ["nohup"] = ShellCommandType.Process,
            ["bg"] = ShellCommandType.Process,
            ["fg"] = ShellCommandType.Process,
            ["jobs"] = ShellCommandType.Process,
            // Network
            ["curl"] = ShellCommandType.Network,
            ["wget"] = ShellCommandType.Network,
            ["ssh"] = ShellCommandType.Network,
            ["scp"] = ShellCommandType.Network,
            ["ping"] = ShellCommandType.Network,
            ["netstat"] = ShellCommandType.Network,
            // PackageManager
            ["apt"] = ShellCommandType.PackageManager,
            ["apt-get"] = ShellCommandType.PackageManager,
            ["brew"] = ShellCommandType.PackageManager,
            ["npm"] = ShellCommandType.PackageManager,
            ["yarn"] = ShellCommandType.PackageManager,
            ["pip"] = ShellCommandType.PackageManager,
            ["pip3"] = ShellCommandType.PackageManager,
            ["cargo"] = ShellCommandType.PackageManager,
            ["gem"] = ShellCommandType.PackageManager,
            // Build / dotnet
            ["make"] = ShellCommandType.Build,
            ["cmake"] = ShellCommandType.Build,
            ["msbuild"] = ShellCommandType.Build,
            ["dotnet"] = ShellCommandType.Build,
            ["csc"] = ShellCommandType.Build,
            ["mcs"] = ShellCommandType.Build,
            // VCS
            ["git"] = ShellCommandType.Vcs,
            ["svn"] = ShellCommandType.Vcs,
            ["hg"] = ShellCommandType.Vcs,
            // Scripting / runtime
            ["sh"] = ShellCommandType.Scripting,
            ["bash"] = ShellCommandType.Scripting,
            ["zsh"] = ShellCommandType.Scripting,
            ["python"] = ShellCommandType.Scripting,
            ["python3"] = ShellCommandType.Scripting,
            ["node"] = ShellCommandType.Scripting,
            ["mono"] = ShellCommandType.Scripting,
        };

    // Regex for inline env vars: KEY=value  (before the executable)
    private static readonly Regex InlineEnvRe =
        new(@"^([A-Za-z_][A-Za-z0-9_]*)=(\S*)$", RegexOptions.Compiled);

    // Regex for redirect tokens: >, >>, 2>&1, &>, etc.
    private static readonly Regex RedirectRe =
        new(@"^(>>|2>&1|&>|>{1,2}|<)(.*)$", RegexOptions.Compiled);

    // Here-doc detection: cat > FILE << 'DELIM'  or  cat >> FILE << DELIM
    private static readonly Regex HereDocStartRe =
        new(@"<<\s*'?(?<delim>\w+)'?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a multi-line terminal string that may contain several commands
    /// separated by <c>&&</c>, <c>||</c>, <c>|</c>, or newlines, and returns
    /// one <see cref="CommandMetadata"/> per logical command.
    /// </summary>
    public static IReadOnlyList<CommandMetadata> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        // Normalize newlines inside quoted strings to spaces before splitting
        input = NormalizeNewlinesInQuotes(input);

        var segments = SplitIntoSegments(input);
        var results = new List<CommandMetadata>(segments.Count);

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrWhiteSpace(seg.Text)) continue;

            var meta = ParseSegment(seg);
            results.Add(meta);
        }

        return results;
    }

    /// <summary>
    /// Replaces newlines inside single- or double-quoted strings with spaces,
    /// so that multi-line quoted arguments (e.g. python -c "...\n...") are
    /// preserved as single tokens after line splitting.
    /// </summary>
    private static string NormalizeNewlinesInQuotes(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        bool inS = false, inD = false;
        foreach (char c in input)
        {
            if (c == '\'' && !inD) { inS = !inS; sb.Append(c); continue; }
            if (c == '"' && !inS) { inD = !inD; sb.Append(c); continue; }
            if ((inS || inD) && c == '\n')
                sb.Append(' ');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private record Segment(string Text, bool IsPiped, bool IsChained, string? WorkingDir);

    /// <summary>
    /// Splits the raw input into logical segments, respecting here-doc bodies
    /// (content between &lt;&lt; DELIM … DELIM must not be split further).
    /// </summary>
    private static List<Segment> SplitIntoSegments(string input)
    {
        // Normalise line continuations: trailing backslash + newline → space
        input = Regex.Replace(input, @"\\\r?\n\s*", " ");

        var lines = input.Split('\n');
        var segments = new List<Segment>();
        bool isPiped = false;
        bool isChained = false;
        string? workDir = null;

        // Accumulate lines; detect here-doc blocks
        string? hereDelim = null;
        var hereAccumulator = new System.Text.StringBuilder();
        string? herePrefix = null; // the line that started the here-doc

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // ── Inside a here-doc body ──────────────────────────────────────
            if (hereDelim != null)
            {
                if (line == hereDelim)
                {
                    // End of here-doc — emit the whole block as one segment
                    string fullBlock = herePrefix + "\n" + hereAccumulator.ToString().TrimEnd() + "\n" + hereDelim;
                    segments.Add(new Segment(fullBlock, isPiped, isChained, workDir));
                    hereDelim = null;
                    hereAccumulator.Clear();
                    herePrefix = null;
                }
                else
                {
                    hereAccumulator.AppendLine(line);
                }
                continue;
            }

            // ── Check for here-doc start ────────────────────────────────────
            var hdMatch = HereDocStartRe.Match(line);
            if (hdMatch.Success)
            {
                hereDelim = hdMatch.Groups["delim"].Value;
                herePrefix = line;
                hereAccumulator.Clear();
                continue;
            }

            // ── Split on && / || / | within the line ───────────────────────
            // We tokenise carefully to avoid splitting inside quotes
            var parts = SplitOnOperators(line);
            foreach (var (text, op) in parts)
            {
                string t = text.Trim();
                if (string.IsNullOrEmpty(t)) { UpdateFlags(op, ref isPiped, ref isChained); continue; }

                // cd DIR && … → record working dir for the next command
                if (t.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) && op == "&&")
                {
                    workDir = t[3..].Trim();
                    UpdateFlags(op, ref isPiped, ref isChained);
                    continue;
                }

                // Use the operator that FOLLOWS this segment for flags
                bool segPiped = op == "|";
                bool segChained = op is "&&" or "||";
                segments.Add(new Segment(t, segPiped, segChained, workDir));

                UpdateFlags(op, ref isPiped, ref isChained);

                // Clear workDir at end of chain (no operator follows)
                if (op == null && workDir != null)
                    workDir = null;
            }
        }

        return segments;
    }

    private static void UpdateFlags(string? op, ref bool piped, ref bool chained)
    {
        piped = op == "|";
        chained = op is "&&" or "||";
    }

    /// <summary>
    /// Splits a single shell line on shell operators <c>|</c>, <c>&&</c>,
    /// <c>||</c> while ignoring those inside single- or double-quoted strings.
    /// Returns (text, followingOperator) pairs.
    /// </summary>
    private static List<(string Text, string? Op)> SplitOnOperators(string line)
    {
        var result = new List<(string, string?)>();
        int start = 0;
        bool inSingle = false, inDouble = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
            if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
            if (inSingle || inDouble) continue;

            // Try to match operator at position i
            string? op = null;
            if (i + 1 < line.Length && line[i..].StartsWith("&&")) op = "&&";
            else if (i + 1 < line.Length && line[i..].StartsWith("||")) op = "||";
            else if (c == '|' && (i + 1 >= line.Length || line[i + 1] != '|')) op = "|";

            if (op != null)
            {
                result.Add((line[start..i], op));
                start = i + op.Length;
                i = start - 1; // loop will i++
            }
        }

        result.Add((line[start..], null));
        return result;
    }

    /// <summary>
    /// Turns a single segment text into a <see cref="CommandMetadata"/>.
    /// </summary>
    private static CommandMetadata ParseSegment(Segment seg)
    {
        string raw = seg.Text.Trim();

        // ── Detect here-doc ────────────────────────────────────────────────
        HereDocInfo? hereDoc = null;
        // Use multiline so $ matches end of any line (not just end of string)
        var hdm = Regex.Match(raw, @"<<\s*'?(?<delim>\w+)'?\s*$", RegexOptions.Multiline);
        if (hdm.Success)
        {
            string delim = hdm.Groups["delim"].Value;
            string beforeHere = raw[..hdm.Index].Trim();
            string afterEnd = raw[(hdm.Index + hdm.Length)..];

            // Extract body: everything after the first newline until the delimiter line
            int bodyStart = afterEnd.IndexOf('\n');
            string body = bodyStart >= 0 ? afterEnd[(bodyStart + 1)..] : string.Empty;
            // Remove trailing delimiter line
            int delimLine = body.LastIndexOf('\n' + delim);
            if (delimLine >= 0) body = body[..delimLine];

            // Extract target file from "cat > FILE <<" 
            string targetFile = string.Empty;
            var redirectInBefore = Regex.Match(beforeHere, @"(>>?)\s*(\S+)");
            if (redirectInBefore.Success)
                targetFile = redirectInBefore.Groups[2].Value;

            hereDoc = new HereDocInfo
            {
                Delimiter = delim,
                TargetFile = targetFile,
                Body = body.Trim(),
            };

            // Continue parsing the part before << as the actual command line
            raw = beforeHere;
        }

        // ── Tokenise (naive, quote-aware) ──────────────────────────────────
        var tokens = Tokenise(raw);
        var inlineEnv = new Dictionary<string, string>();
        var flags = new List<string>();
        var positionals = new List<string>();
        var redirects = new List<string>();
        string? scriptBody = null;

        // Consume leading KEY=value pairs
        int idx = 0;
        while (idx < tokens.Count)
        {
            var m = InlineEnvRe.Match(tokens[idx]);
            if (!m.Success) break;
            inlineEnv[m.Groups[1].Value] = m.Groups[2].Value;
            idx++;
        }

        string executable = idx < tokens.Count ? tokens[idx++] : string.Empty;
        // Strip leading path: /usr/bin/dotnet → dotnet
        string execName = System.IO.Path.GetFileName(executable);

        // Collect remaining tokens, extracting script body for -c / -e flags
        while (idx < tokens.Count)
        {
            string tok = tokens[idx++];

            // Script body extraction: -c or -e followed by quoted script
            if ((tok == "-c" || tok == "-e") && idx < tokens.Count)
            {
                flags.Add(tok);
                scriptBody = tokens[idx++].Trim('\"', '\'');
                continue;
            }

            // Redirect operators (standalone > >> or combined 2>&1)
            var redirM = RedirectRe.Match(tok);
            if (redirM.Success)
            {
                redirects.Add(tok);
                // Next token may be the file target
                if (redirM.Groups[2].Value.Length == 0 && idx < tokens.Count)
                    redirects.Add(tokens[idx++]);
                continue;
            }

            if (tok.StartsWith('-'))
                flags.Add(tok);
            else
                positionals.Add(tok);
        }

        var allArgs = tokens.Skip(tokens.IndexOf(executable) + 1)
                            .Where(t => t != executable)
                            .ToList();

        // ── Classify type ─────────────────────────────────────────────────
        ShellCommandType type = ClassifyCommand(execName, hereDoc, seg, positionals, flags);

        return new CommandMetadata
        {
            Raw = seg.Text.Trim(),
            Executable = executable,
            Arguments = allArgs,
            Flags = flags,
            Positionals = positionals,
            Redirects = redirects,
            Type = type,
            IsPiped = seg.IsPiped,
            IsChained = seg.IsChained,
            WorkingDirectory = seg.WorkingDir,
            HereDoc = hereDoc,
            InlineEnv = inlineEnv,
            ScriptBody = scriptBody,
        };
    }

    private static ShellCommandType ClassifyCommand(
        string execName, HereDocInfo? hereDoc,
        Segment seg, List<string> positionals, List<string> flags)
    {
        if (hereDoc != null)
            return ShellCommandType.HereDoc;

        if (ExecutableMap.TryGetValue(execName, out var mapped))
        {
            // dotnet build/run/test → Build; dotnet add/restore → PackageManager
            if (execName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                positionals.Count > 0 &&
                positionals[0] is "add" or "restore" or "nuget")
                return ShellCommandType.PackageManager;

            return mapped;
        }

        if (seg.IsPiped) return ShellCommandType.Pipe;
        if (seg.IsChained) return ShellCommandType.Chain;
        return ShellCommandType.Unknown;
    }

    /// <summary>
    /// Very simple quote-aware tokeniser (handles single and double quotes,
    /// does NOT expand variables).
    /// </summary>
    private static List<string> Tokenise(string line)
    {
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inS = false, inD = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\'' && !inD) { inS = !inS; cur.Append(c); continue; }
            if (c == '"' && !inS) { inD = !inD; cur.Append(c); continue; }

            if (!inS && !inD && char.IsWhiteSpace(c))
            {
                if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
                continue;
            }

            cur.Append(c);
        }

        if (cur.Length > 0) tokens.Add(cur.ToString());
        return tokens;
    }
}