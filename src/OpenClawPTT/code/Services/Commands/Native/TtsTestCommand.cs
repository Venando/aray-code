using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /tts-test — tests the TTS summarization pipeline.</summary>
public sealed class TtsTestCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly ITtsSummarizer? _ttsSummarizer;
    private readonly AppConfig _appConfig;

    public string Name => "tts-test";
    public string Description => "Test TTS summarization pipeline with sample file";
    public CommandSource Source => CommandSource.Native;
    public CommandType Type => CommandType.DirectLlm;
    public string[]? Suggestions => null;

    public TtsTestCommand(IStreamShellHost host, ITtsSummarizer? ttsSummarizer, AppConfig appConfig)
    {
        _host = host;
        _ttsSummarizer = ttsSummarizer;
        _appConfig = appConfig;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (_ttsSummarizer == null)
        {
            _host.AddMessage("[yellow]  TTS summarizer not available. Make sure DirectLlmUrl is configured.[/]");
            return;
        }

        var samplePath = Path.Combine(AppContext.BaseDirectory, "test-summary-sample.txt");
        if (!File.Exists(samplePath))
        {
            _host.AddMessage($"[red]  Sample file not found: {samplePath}[/]");
            return;
        }

        var rawText = await File.ReadAllTextAsync(samplePath, ct);
        _host.AddMessage($"[grey]  Loaded sample ({rawText.Length} chars raw)[/]");

        _host.AddMessage("[grey]  Running through TTS preprocessing...[/]");
        var preprocessed = TtsContentFilter.SanitizeForTts(rawText);
        _host.AddMessage($"[grey]  After sanitize: {preprocessed.Length} chars[/]:[white]{preprocessed}[/]");

        _host.AddMessage($"[grey]  Sending to LLM ({_appConfig.DirectLlmModelName}) for summarization...[/]");
        try
        {
            var summarized = await _ttsSummarizer.SummarizeForTtsAsync(rawText, _appConfig, ct);
            _host.AddMessage($"[green]  Summary ({summarized.Length} chars):[/]");
            _host.AddMessage($"  {Markup.Escape(summarized)}");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Summarization failed: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
