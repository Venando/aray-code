using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Native command: /llm — direct LLM interaction with three modes:
///   <c>/llm message &lt;text&gt;</c> — send a message directly to the configured LLM.
///   <c>/llm summary-test</c>   — test TTS summarization pipeline with sample file.
///   <c>/llm title-test</c>    — test conversation naming pipeline with sample file.
/// </summary>
public sealed class LlmCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IColorConsole _console;
    private readonly IDirectLlmService? _directLlmService;
    private readonly AppConfig _appConfig;
    private readonly ITtsSummarizer? _ttsSummarizer;
    private readonly IConversationNamingService? _namingService;

    public string Name => "llm";
    public string Description => "<message|summary-test|title-test> Send message to LLM, test TTS summary, or test naming";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.DirectLlm;
    public string[]? Suggestions => new[] { "message", "summary-test", "title-test" };

    public LlmCommand(
        IStreamShellHost host,
        IColorConsole console,
        IDirectLlmService? directLlmService,
        AppConfig appConfig,
        ITtsSummarizer? ttsSummarizer = null,
        IConversationNamingService? namingService = null)
    {
        _host = host;
        _console = console;
        _directLlmService = directLlmService;
        _appConfig = appConfig;
        _ttsSummarizer = ttsSummarizer;
        _namingService = namingService;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var subcommand = args[0].ToLowerInvariant();

        switch (subcommand)
        {
            case "message":
                await ExecuteMessageAsync(args.Skip(1).ToArray(), ct);
                break;

            case "summary-test":
                await ExecuteSummaryTestAsync(ct);
                break;

            case "title-test":
                await ExecuteTitleTestAsync(ct);
                break;

            default:
                // Treat unrecognized subcommand as a message (backward compat)
                await ExecuteMessageAsync(args, ct);
                break;
        }
    }

    // ── Mode: message ──────────────────────────────────────────────────────────

    private async Task ExecuteMessageAsync(string[] messageArgs, CancellationToken ct)
    {
        if (_directLlmService == null || !_directLlmService.IsConfigured)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Direct LLM is not configured. Set DirectLlmUrl and DirectLlmModelName in config.[/]");
            return;
        }

        var message = string.Join(" ", messageArgs);
        if (string.IsNullOrWhiteSpace(message))
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Usage: /llm message <your text>[/]");
            return;
        }

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Sending to LLM ({_appConfig.DirectLlmModelName})...[/]");

        try
        {
            var response = await _directLlmService.SendAsync(message, ct);
            _console.PrintFormatted($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  LLM Response:[/] ", response);
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  LLM request failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    // ── Mode: summary-test ─────────────────────────────────────────────────────

    private async Task ExecuteSummaryTestAsync(CancellationToken ct)
    {
        if (_ttsSummarizer == null)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  TTS summarizer not available. Make sure DirectLlmUrl is configured.[/]");
            return;
        }

        var samplePath = Path.Combine(AppContext.BaseDirectory, "test-summary-sample.txt");
        if (!File.Exists(samplePath))
        {
            // Fall back to source-relative path for development builds
            samplePath = FindSampleFile("test-summary-sample.txt");
            if (samplePath == null)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Sample file not found. Ensure test-summary-sample.txt is in the output directory.[/]");
                return;
            }
        }

        var rawText = await File.ReadAllTextAsync(samplePath, ct);
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Loaded sample ({rawText.Length} chars raw)[/]");

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Running through TTS preprocessing...[/]");
        var preprocessed = TtsContentFilter.SanitizeForTts(rawText);
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  After sanitize: {preprocessed.Length} chars[/]:[{ThemeProvider.Current.Tools.General.Value}]{preprocessed}[/]");

        if (_directLlmService == null || !_directLlmService.IsConfigured)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Direct LLM not configured — skipping summarization step.[/]");
            return;
        }

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Sending to LLM ({_appConfig.DirectLlmModelName}) for summarization...[/]");
        try
        {
            var summarized = await _ttsSummarizer.SummarizeForTtsAsync(rawText, _appConfig, ct);
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Summary ({summarized.Length} chars):[/]");
            _host.AddMessage($"  {Markup.Escape(summarized)}");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Summarization failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    // ── Mode: title-test ──────────────────────────────────────────────────────

    private async Task ExecuteTitleTestAsync(CancellationToken ct)
    {
        if (_directLlmService == null || !_directLlmService.IsConfigured)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Direct LLM is not configured. Set DirectLlmUrl and DirectLlmModelName in config.[/]");
            return;
        }

        var samplePath = Path.Combine(AppContext.BaseDirectory, "test-conversation-sample.txt");
        if (!File.Exists(samplePath))
        {
            samplePath = FindSampleFile("test-conversation-sample.txt");
            if (samplePath == null)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Sample file not found. Ensure test-conversation-sample.txt is in the output directory.[/]");
                return;
            }
        }

        var rawText = await File.ReadAllTextAsync(samplePath, ct);
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Loaded conversation sample ({rawText.Length} chars)[/]");

        // Build a naming prompt that mimics what the naming service would build
        var prompt = BuildTitleTestPrompt(rawText);

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Sending to LLM ({_appConfig.DirectLlmModelName}) for title generation...[/]");
        try
        {
            var response = await _directLlmService.SendAsync(prompt, ct);
            var cleaned = SanitizeTitleResponse(response);
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Generated Title:[/] [{ThemeProvider.Current.Tools.Messages.Emphasis}]{Markup.Escape(cleaned)}[/]");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Title generation failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static string BuildTitleTestPrompt(string sampleText)
    {
        // Parse the sample file: role/header lines followed by content
        var lines = sampleText.Split('\n');
        var conversation = new System.Text.StringBuilder();

        conversation.AppendLine("Based on the following conversation, generate a short 4-6 word descriptive title.");
        conversation.AppendLine("Return ONLY the title, no quotes, no explanation, no punctuation at the end.");
        conversation.AppendLine();
        conversation.AppendLine("Conversation:");

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;
            conversation.AppendLine($"  {trimmed}");
        }

        return conversation.ToString();
    }

    private static string SanitizeTitleResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response == "(No response)")
            return response;

        var cleaned = response.Trim();

        // Remove common LLM wrappers
        for (int i = 0; i < 3; i++)
        {
            if (cleaned.StartsWith('"') && cleaned.EndsWith('"'))
                cleaned = cleaned[1..^1].Trim();
            else if (cleaned.StartsWith('\u201C') && cleaned.EndsWith('\u201D'))
                cleaned = cleaned[1..^1].Trim();
            else
                break;
        }

        if (cleaned.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Title:".Length..].Trim();
        if (cleaned.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Name:".Length..].Trim();

        cleaned = cleaned.Replace("\"", "").Replace("\u201C", "").Replace("\u201D", "").Trim();
        cleaned = cleaned.TrimEnd('.', '!', '?', ':', ';', ',');

        return cleaned;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ShowUsage()
    {
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Usage:[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Value}]/llm message <text>[/]      Send a message to the configured LLM");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Value}]/llm summary-test[/]        Test the TTS summarization pipeline");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Value}]/llm title-test[/]          Test the conversation naming pipeline");
    }

    /// <summary>
    /// Searches upward from the output directory for sample files in development builds.
    /// </summary>
    private static string? FindSampleFile(string fileName)
    {
        // Try walking up from the output directory to find the project root
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            dir = Path.GetDirectoryName(dir);
            if (dir == null) break;

            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;

            // Also check the src directory
            var srcCandidate = Path.Combine(dir, "src", "OpenClawPTT", fileName);
            if (File.Exists(srcCandidate))
                return srcCandidate;
        }

        return null;
    }
}
