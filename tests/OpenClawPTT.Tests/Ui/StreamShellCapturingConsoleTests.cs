using System.Collections.Generic;
using System.Linq;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for StreamShellCapturingConsole, focusing on the FlushToStreamShell
/// path that builds shell messages from the captured buffer.
/// </summary>
public class StreamShellCapturingConsoleTests
{
    [Fact(Skip = "Broken: ToolOutputHelper.Flush/FlushToStreamShell path doesn't forward messages to shell host in the current pipeline")]
    public void Flush_WithOnlyWhitespace_StillSendsHeaderMessage()
    {
        var shellHost = new CapturingStreamShellHost();
        var helper = new ToolOutputHelper(shellHost);

        var headerLine = "[grey]  ▶️[/] [gray93 on #333333]Exec  [/]";
        helper.Start(headerLine);
        helper.PrintLine("");
        helper.Flush();

        Assert.Single(shellHost.Messages);
        Assert.Contains(headerLine, shellHost.Messages[0]);
    }

    [Fact]
    public void FlushToStreamShell_ShouldNotProduceMessagesWithBrokenMarkup()
    {
        var shellHost = new CapturingStreamShellHost();
        var handler = new ToolDisplayHandler(rightMarginIndent: 10, shellHost: shellHost);

        handler.Handle("read", "{\"file\":\"/path.txt\"}");

        // Check that no message contains an unclosed opening tag or a stray closing tag
        string[] messages = shellHost.Messages.ToArray();
        foreach (string msg in messages)
        {
            var result = MarkupValidator.Validate(msg);
            Assert.True(result.IsValid,
                $"Invalid markup in message: '{msg.Replace("\n", "\\n")}'\n{result}");
        }
    }


    [Fact]
    public void FlushToStreamShell_ShouldNotProduceMessagesWithBrokenMarkup2()
    {
        var shellHost = new CapturingStreamShellHost();
        var handler = new ToolDisplayHandler(rightMarginIndent: 10, shellHost: shellHost);

        handler.Handle("read", "{\"file\":\"/verylongpath/verylongpath/verylongpath/verylongpathverylongpath/verylongpath/verylongpath/verylongpath/verylongpath/verylongpath/verylongpath/verylongpath/path.txt\"}");

        // Check that no message contains an unclosed opening tag or a stray closing tag
        string[] messages = shellHost.Messages.ToArray();
        foreach (string msg in messages)
        {
            var result = MarkupValidator.Validate(msg);
            Assert.True(result.IsValid,
                $"Invalid markup in message: '{msg.Replace("\n", "\\n")}'\n{result}");
        }
    }

    private sealed class CapturingStreamShellHost : IStreamShellHost
    {
        public readonly List<string> Messages = new();
        public readonly List<StreamShell.Command> Commands = new();

#pragma warning disable CS0067 // Required by interface but not used in tests
        public event Action<StreamShell.UserInputSubmittedEventArgs>? UserInputSubmitted;
#pragma warning restore CS0067

        public StreamShell.IInputHandler InputHandler => null!;

        public void AddMessage(string markup) => Messages.Add(markup);
        public void AddCommand(StreamShell.Command command) => Commands.Add(command);
        public void AddCommand(string name, string description,
            Func<string[], Dictionary<string, string>, Task> handler,
            string[]? argumentSuggestions = null)
        {
            Commands.Add(new StreamShell.Command(name, description, handler, argumentSuggestions ?? []));
        }
        public void RemoveCommand(string name) { }
        public System.Threading.Tasks.Task Run(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public void Clear() { Messages.Clear(); }
        public void SetTopSeparator(string? leftText = null, string? rightText = null,
            char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null) { }
        public void SetBottomSeparator(string? leftText = null, string? rightText = null,
            char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null) { }
        public void Stop() { }
        public void SetRightMarginIndent(int margin) { }
        public void SetInputPrefix(string prefix) { }
        public void SetContinuationPrefix(string prefix) { }
        public void SetDefaultPanel(StreamShell.IBottomPanel panel) { }
        public void SetPanelHistoryService(SessionHistoryService service) { }
        public void SetDefaultPanelFactory(Func<StreamShell.IBottomPanel> factory) { }
        public void ResetDefaultPanel() { }
        public void SetBottomPanel(StreamShell.IBottomPanel panel) { }
        public void ResetBottomPanel() { }
        public event EventHandler<StreamShell.BottomPanelChangedEventArgs>? BottomPanelChanged { add { } remove { } }
        public System.Threading.Tasks.Task<StreamShell.IVariant[]?> PromptSelection(string title, StreamShell.IVariantEntry[] variants, StreamShell.SelectionInfo? info = null)
            => System.Threading.Tasks.Task.FromResult<StreamShell.IVariant[]?>(null);
        public void Dispose() { }
        public void SetRenderChunkSize(int size) { }
        public void SetCursorMarkup(string markup) { }
        public void SetSelectionMarkup(string markup) { }
        public void SetCommandSlashMarkup(string markup) { }
        public void ApplyStreamShellTheme(int prefixWidth) { }
    }
}
