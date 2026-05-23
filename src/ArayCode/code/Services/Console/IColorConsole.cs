namespace ArayCode.Services;

/// <summary>
/// Abstraction for colored console output. Provides methods for printing
/// various UI elements, messages, and logging through a StreamShell host.
/// </summary>
public interface IColorConsole
{
    // ── Banner and Help ────────────────────────────────────────
    
    /// <summary>Prints the application banner.</summary>
    void PrintBanner();
    
    /// <summary>Prints the help menu with agent introduction.</summary>
    void PrintHelpMenu(AppConfig appConfig);
    
    /// <summary>Prints the agent introduction panel.</summary>
    void PrintAgentIntroduction(AppConfig appConfig);
    
    // ── General Output ─────────────────────────────────────────
    
    /// <summary>Send a raw Spectre markup message to the StreamShell output.</summary>
    void PrintMarkup(string markup);
    
    /// <summary>Display user's own text message.</summary>
    void PrintUserMessage(string text);

    /// <summary>Spectre markup prefix for user messages (e.g. " [green] You:[/] ").</summary>
    string UserMessagePrefix { get; set; }

    /// <summary>Pre-computed right-edge margin in characters (max of config indent and 10% console width).</summary>
    int ReservedRightMargin { get; set; }

    void PrintFormatted(string prefix, string text);

    /// <summary>Display user's message with pre-formatted markup.</summary>
    void PrintMarkupedUserMessage(string text);
    
    // ── Status Messages ────────────────────────────────────────
    
    /// <summary>Writes a debug/log message at grey level.</summary>
    void PrintInfo(string message);
    
    /// <summary>Prints a success message with checkmark.</summary>
    void PrintSuccess(string message);
    
    /// <summary>Prints a success message with word wrap support.</summary>
    void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent);
    
    /// <summary>Prints a warning message.</summary>
    void PrintWarning(string message);
    
    /// <summary>Prints an error message.</summary>
    void PrintError(string message);
    
    /// <summary>Prints the recording indicator when actively recording.</summary>
    void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk);
    
    // ── Agent Replies ──────────────────────────────────────────
    
    /// <summary>Prints an agent reply with prefix and body.</summary>
    void PrintAgentReply(string prefix, string body);
    
    /// <summary>Prints an agent reply with markdown formatting.</summary>
    void PrintAgentReplyWithMarkdown(string prefix, string body);
    
    /// <summary>Prints a streaming delta from agent reply.</summary>
    void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix);
    
    /// <summary>Prints a streaming delta from agent reply with config.</summary>
    void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config);
    
    // ── Gateway Errors ─────────────────────────────────────────
    
    /// <summary>Prints a gateway error with optional detail code and recommended step.</summary>
    void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null);

    /// <summary>Prints a model fallback warning showing the provider change.</summary>
    void PrintModelFallback(string fromProvider, string fromModel, string toProvider, string toModel, bool isQuotaError);

    /// <summary>Prints a model failure notification when all fallbacks are exhausted.</summary>
    void PrintModelFailed(string errorMessage);
    
    // ── Logging ────────────────────────────────────────────────

    /// <summary>Applies terminal display configuration from an AppConfig.
    /// Computes the right-edge margin, sets up input prompt prefixes, and updates
    /// console properties to match the loaded configuration.</summary>
    void ApplyConsoleConfig(AppConfig config);

    /// <summary>Gets or sets the current log level threshold. Messages below this level are suppressed.</summary>
    LogLevel LogLevel { get; set; }

    /// <summary>Logs a message with the specified tag and severity level.</summary>
    void Log(string tag, string msg, LogLevel level = LogLevel.Debug);
    
    /// <summary>Logs a success message with the specified tag and severity level.</summary>
    void LogOk(string tag, string msg, LogLevel level = LogLevel.Info);
    
    /// <summary>Logs an error message with the specified tag.</summary>
    void LogError(string tag, string msg);
    
    // ── StreamShell Access ─────────────────────────────────────
    
    /// <summary>Gets the underlying StreamShell host for capturing formatter output.</summary>
    IStreamShellHost? GetStreamShellHost();
}
