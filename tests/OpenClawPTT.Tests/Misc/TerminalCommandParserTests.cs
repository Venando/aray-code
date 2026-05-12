using Xunit;

namespace OpenClawPTT.Tests;

public class TerminalCommandParserTests
{
    [Fact]
    public void Parse_SimpleLs_NoFlags()
    {
        var result = TerminalCommandParser.Parse("ls");
        Assert.Single(result);
        Assert.Equal("ls", result[0].Executable);
        Assert.Equal(ShellCommandType.FileSystem, result[0].Type);
        Assert.Empty(result[0].Flags);
        Assert.Empty(result[0].Positionals);
    }

    [Fact]
    public void Parse_LsWithFlags()
    {
        var result = TerminalCommandParser.Parse("ls -la --color=auto");
        Assert.Single(result);
        Assert.Equal("ls", result[0].Executable);
        Assert.Equal(2, result[0].Flags.Count);
        Assert.Contains("-la", result[0].Flags);
        Assert.Contains("--color=auto", result[0].Flags);
    }

    [Fact]
    public void Parse_GrepWithRegex()
    {
        var result = TerminalCommandParser.Parse("grep \"Markup\\.\" file.txt");
        Assert.Single(result);
        Assert.Equal("grep", result[0].Executable);
        Assert.Equal(ShellCommandType.FileContent, result[0].Type);
        // Both the regex pattern and file are positionals
        Assert.Equal(2, result[0].Positionals.Count);
        Assert.Contains("file.txt", result[0].Positionals);
    }

    [Fact]
    public void Parse_ChainedCommands()
    {
        var result = TerminalCommandParser.Parse("cd /tmp && ls -la && cat file.txt");
        Assert.Equal(2, result.Count);
        Assert.Equal("ls", result[0].Executable);
        Assert.Equal("/tmp", result[0].WorkingDirectory);
        Assert.True(result[0].IsChained);
        Assert.Equal("cat", result[1].Executable);
        Assert.Equal("/tmp", result[1].WorkingDirectory);
        Assert.False(result[1].IsChained);
    }

    [Fact]
    public void Parse_PipedCommands()
    {
        var result = TerminalCommandParser.Parse("cat file.txt | grep pattern | wc -l");
        Assert.Equal(3, result.Count);
        Assert.Equal("cat", result[0].Executable);
        Assert.True(result[0].IsPiped);  // cat outputs to pipe
        Assert.Equal("grep", result[1].Executable);
        Assert.True(result[1].IsPiped);  // grep outputs to pipe
        Assert.Equal("wc", result[2].Executable);
        Assert.False(result[2].IsPiped);  // wc is the final receiver
    }

    [Fact]
    public void Parse_PythonInlineScript()
    {
        var result = TerminalCommandParser.Parse("python3 -c \"print('hello')\"");
        Assert.Single(result);
        Assert.Equal("python3", result[0].Executable);
        Assert.Equal(ShellCommandType.Scripting, result[0].Type);
        Assert.Equal("print('hello')", result[0].ScriptBody);
    }

    [Fact]
    public void Parse_PythonMultilineScript()
    {
        var cmd = "python3 -c \"\nimport re, sys\nsys.path.insert(0, '.')\n\n# Check\nm = re.search(r'\\[(\\d{2,3})\\]', 'test[01]')\nprint(f'Result: {m.group(1) if m else None}')\n\"";
        var result = TerminalCommandParser.Parse(cmd);
        Assert.Single(result);
        Assert.Equal("python3", result[0].Executable);
        Assert.Equal(ShellCommandType.Scripting, result[0].Type);
        Assert.NotNull(result[0].ScriptBody);
        Assert.Contains("import re, sys", result[0].ScriptBody!);
    }

    [Fact]
    public void Parse_DotnetBuild()
    {
        var result = TerminalCommandParser.Parse("dotnet build -v q");
        Assert.Single(result);
        Assert.Equal("dotnet", result[0].Executable);
        Assert.Equal(ShellCommandType.Build, result[0].Type);
        Assert.Contains("build", result[0].Positionals);
        Assert.Contains("-v", result[0].Flags);
        Assert.Contains("q", result[0].Positionals);
    }

    [Fact]
    public void Parse_EnvVarsAndRedirects()
    {
        var result = TerminalCommandParser.Parse("FOO=bar DEBUG=1 ./script.sh 2>&1 > output.log");
        Assert.Single(result);
        Assert.Equal("./script.sh", result[0].Executable);
        Assert.Equal(2, result[0].InlineEnv.Count);
        Assert.Equal("bar", result[0].InlineEnv["FOO"]);
        Assert.Equal("1", result[0].InlineEnv["DEBUG"]);
        Assert.Equal(2, result[0].Redirects.Count);
    }

    [Fact]
    public void Parse_NodeInlineScript()
    {
        var result = TerminalCommandParser.Parse("node -e \"console.log('hello')\"");
        Assert.Single(result);
        Assert.Equal("node", result[0].Executable);
        Assert.Equal(ShellCommandType.Scripting, result[0].Type);
        Assert.Equal("console.log('hello')", result[0].ScriptBody);
    }

    [Fact]
    public void Parse_PythonComplexTorrentScript()
    {
        var cmd = "python3 -c \"\nimport sys\nsys.path.insert(0, '.')\nimport importlib, json, re\n\nimport find_and_queue\nimportlib.reload(find_and_queue)\n\ntorrents = [\n {'title': '[SubsPlease] Kill Ao - 04', 'downloads': 2712}\n]\n\nfrom find_and_queue import filter_torrents\nfiltered = filter_torrents(torrents=torrents)\nprint(f'Count: {len(filtered)}')\n\"";
        var result = TerminalCommandParser.Parse(cmd);
        Assert.Single(result);
        Assert.Equal("python3", result[0].Executable);
        Assert.NotNull(result[0].ScriptBody);
        Assert.Contains("import sys", result[0].ScriptBody!);
        Assert.Contains("filter_torrents", result[0].ScriptBody!);
    }

    [Fact]
    public void Parse_CdWithWorkingDir()
    {
        var result = TerminalCommandParser.Parse("cd /home/user && ls");
        Assert.Single(result);
        Assert.Equal("ls", result[0].Executable);
        Assert.Equal("/home/user", result[0].WorkingDirectory);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = TerminalCommandParser.Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TerminalCommandParser.Parse("   \n  \t  ");
        Assert.Empty(result);
    }
}
