namespace OpenClawPTT.Services;

/// <summary>
/// Summarizes text for TTS using Direct LLM.
/// </summary>
public interface ITtsSummarizer : IDisposable
{
    /// <summary>
    /// Summarizes text for TTS output.
    /// </summary>
    /// <param name="text">Text to summarize</param>
    /// <param name="config">App config with summarization settings</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summarized text ready for TTS</returns>
    Task<string> SummarizeForTtsAsync(string text, AppConfig config, CancellationToken ct = default);
}

/// <summary>
/// Uses Direct LLM to summarize content for TTS.
/// </summary>
public sealed class TtsSummarizer : ITtsSummarizer, IDisposable
{
    private readonly IDirectLlmService? _directLlm;
    private readonly IColorConsole? _console;
    private bool _disposed;

    public TtsSummarizer(IDirectLlmService? directLlm, IColorConsole? console = null)
    {
        _directLlm = directLlm;
        _console = console;
    }

    public async Task<string> SummarizeForTtsAsync(string text, AppConfig config, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TtsSummarizer));
        if (_directLlm == null || !_directLlm.IsConfigured)
            throw new InvalidOperationException("Direct LLM not configured");
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (config.TtsMaxChars <= 0)
            throw new ArgumentException("TtsMaxChars must be greater than 0", nameof(config));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _console?.PrintMarkup($"[grey]  Summarizing for TTS ([bold]{config.DirectLlmModelName}[/])...[/] ");
        var prompt = BuildSummarizationPrompt(text, config);
        var summary = await _directLlm.SendAsync(prompt, ct);
        sw.Stop();

        _console?.PrintFormatted("[gray]  Summary: [/]", summary);
        _console?.PrintMarkup($"[grey]  TTS summary: [bold]{text.Length}[/] → [bold]{summary.Trim().Length}[/] chars in [bold]{sw.ElapsedMilliseconds}ms[/][/]");
        if (string.IsNullOrWhiteSpace(summary) || summary == "(No response)")
            throw new InvalidOperationException("LLM returned no usable content for summarization");

        return TtsContentFilter.SanitizeForTts(summary.Trim());
    }

    /// <summary>
    /// Pre-processes text before LLM summarization — strips markdown and URLs
    /// so the LLM gets cleaner input and doesn't waste tokens on formatting noise.
    /// </summary>
    private static string PreprocessForLlm(string text)
    {
        return TtsContentFilter.SanitizeForTts(text);
    }

    private static string BuildSummarizationPrompt(string text, AppConfig config)
    {
        var codeBlockInstruction = config.TtsCodeBlockMode.ToLowerInvariant() switch
        {
            "summarize" => "For code blocks: describe what the code does in one sentence",
            "skip" => "For code blocks: replace with '[Code block]' or mention the filename if present",
            "smart" => "For code blocks: small snippets (<5 lines) describe key logic; large blocks summarize in one sentence",
            _ => "For code blocks: describe what the code does"
        };

        // Pre-process: strip markdown, code blocks, URLs before sending to LLM
        var cleanText = PreprocessForLlm(text);

        return $@"Act as a concise narrator. Summarize the text below specifically for clear text-to-speech reading. 

Rules:
- NO markdown (no asterisks, hashtags, or bolding).
- Use short, punchy sentences.
- Avoid lists or bullet points; use flowing paragraphs instead.
- Use simple, phonetic words (avoid complex jargon or long acronyms).
- {codeBlockInstruction}
- Output ONLY the summary. Do not say 'Here is the summary' or use quotes.
- Write out all numbers, dates, and times in full natural language (e.g., write 'eleven thirty P-M' instead of '11:30 PM', and 'twenty-twenty-six' instead of '2026').
- Convert all symbols to words (e.g., write 'percent' for '%', and 'dollars' for '$').

Text to summarize:
{cleanText}";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
