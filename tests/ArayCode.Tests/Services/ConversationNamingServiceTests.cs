using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ArayCode.Services;
using ArayCode.Services.Commands;
using Xunit;

namespace ArayCode.Tests.Services;

[Collection("AgentRegistryCollection")]
public class ConversationNamingServiceTests : IDisposable
{
    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            ConversationNamingPrompt =
                "Give a very short 2-4 word descriptive name for a conversation that starts with this message. Return ONLY the name, no quotes, no explanation, no punctuation at the end.\n\nMessage: {message}"
        };
    }

    public ConversationNamingServiceTests()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>
        {
            new AgentInfo { AgentId = "test-agent", Name = "TestAgent", SessionKey = "test-session" }
        });
        AgentRegistry.SetActiveAgent("test-agent");
    }

    public void Dispose()
    {
        AgentRegistry.Deactivate();
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    [Fact]
    public void GetCurrentConversationName_WhenNoNameGenerated_ReturnsNull()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());

        var name = service.GetCurrentConversationName();
        Assert.Null(name);
    }

    [Fact]
    public void OnMessageSent_TriggersNameGeneration_WhenDirectLlmConfigured()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Code Review");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        string? capturedName = null;
        service.ConversationNameChanged += name =>
        {
            capturedName = name;
            ev.Set();
        };

        // Need 3 messages to trigger initial naming (InitialNamingThreshold = 3)
        service.OnMessageSent("Can you review this C# code for me?");
        service.OnMessageSent("Here is the second message");
        service.OnMessageSent("And a third one");
        ev.Wait(TimeSpan.FromSeconds(2));

        Assert.Equal("Code Review", capturedName);
        Assert.Equal("Code Review", service.GetCurrentConversationName());
    }

    [Fact]
    public async Task OnMessageSent_DoesNothing_WhenDirectLlmNotConfigured()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(false);

        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());

        service.OnMessageSent("Any message");
        await Task.Delay(50); // small yield for any async processing

        Assert.Null(service.GetCurrentConversationName());
    }

    [Fact]
    public void OnMessageSent_OnlyGeneratesNameOnce_PerSession()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        service.ConversationNameChanged += _ => ev.Set();

        // Send 3 messages to trigger naming once
        service.OnMessageSent("First message");
        service.OnMessageSent("Second message");
        service.OnMessageSent("Third message");
        ev.Wait(TimeSpan.FromSeconds(2));

        mockLlm.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SetConversationName_SanitizesName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"Name:\" \"Code Review\"");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        string? capturedName = null;
        service.ConversationNameChanged += name =>
        {
            capturedName = name;
            ev.Set();
        };

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));

        Assert.Equal("Code Review", capturedName);
    }

    [Fact]
    public void OnCommandExecuted_Reset_ClearsConversationName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        string? capturedName = "initial";
        service.ConversationNameChanged += name =>
        {
            capturedName = name;
            ev.Set();
        };

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "reset", CommandSource.OpenClaw, ArayCode.Services.Commands.ShellCommandType.SessionControl,
            Array.Empty<string>(), new Dictionary<string, string>()));

        Assert.Null(service.GetCurrentConversationName());
        Assert.Null(capturedName);
    }

    [Fact]
    public void OnCommandExecuted_New_ClearsConversationName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        service.ConversationNameChanged += _ => ev.Set();

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "new", CommandSource.OpenClaw, ArayCode.Services.Commands.ShellCommandType.SessionControl,
            Array.Empty<string>(), new Dictionary<string, string>()));

        Assert.Null(service.GetCurrentConversationName());
    }

    [Fact]
    public void OnCommandExecuted_UnknownCommand_DoesNotClearName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        service.ConversationNameChanged += _ => ev.Set();

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "config", CommandSource.OpenClaw, ArayCode.Services.Commands.ShellCommandType.Admin,
            Array.Empty<string>(), new Dictionary<string, string>()));

        Assert.Equal("Test Name", service.GetCurrentConversationName());
    }

    [Fact]
    public void OnCommandExecuted_IsCaseInsensitive()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        service.ConversationNameChanged += _ => ev.Set();

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "RESET", CommandSource.OpenClaw, ArayCode.Services.Commands.ShellCommandType.SessionControl,
            Array.Empty<string>(), new Dictionary<string, string>()));

        Assert.Null(service.GetCurrentConversationName());
    }

    [Fact]
    public void OnCommandExecuted_NonSessionControlType_DoesNotClearName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, CreateDefaultConfig());
        service.ConversationNameChanged += _ => ev.Set();

        service.OnMessageSent("Message 1");
        service.OnMessageSent("Message 2");
        service.OnMessageSent("Message 3");
        ev.Wait(TimeSpan.FromSeconds(2));
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        // A native "reset" command that is NOT SessionControl should not clear
        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "reset", CommandSource.Native, ArayCode.Services.Commands.ShellCommandType.Unknown,
            Array.Empty<string>(), new Dictionary<string, string>()));

        // Still named because type check fails
        Assert.Equal("Test Name", service.GetCurrentConversationName());
    }

    [Fact]
    public void UsesCustomPromptFromConfig()
    {
        var config = new AppConfig
        {
            ConversationNamingPrompt = "You are a title generator. Output a short title for: {message}"
        };

        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string prompt, CancellationToken _) =>
            {
                // Verify the prompt uses the custom template with the message substituted
                Assert.DoesNotContain("2-4 word", prompt);
                Assert.Contains("title generator", prompt);
                Assert.Contains("Using AI", prompt);
                return Task.FromResult("Custom Title")!;
            });

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, config);
        string? capturedName = null;
        service.ConversationNameChanged += name =>
        {
            capturedName = name;
            ev.Set();
        };

        service.OnMessageSent("Using AI to generate code");
        service.OnMessageSent("Second message");
        service.OnMessageSent("Third message");
        ev.Wait(TimeSpan.FromSeconds(2));

        Assert.Equal("Custom Title", capturedName);
    }

    [Fact]
    public void FallsBackToDefaultPrompt_WhenConfigPromptIsEmpty()
    {
        var config = new AppConfig
        {
            ConversationNamingPrompt = ""
        };

        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string prompt, CancellationToken _) =>
            {
                Assert.Contains("4-6 word", prompt);
                Assert.Contains("User: Hello", prompt);
                Assert.Contains("Current conversation", prompt);
                return Task.FromResult("Default Fallback")!;
            });

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, config);
        string? capturedName = null;
        service.ConversationNameChanged += name =>
        {
            capturedName = name;
            ev.Set();
        };

        service.OnMessageSent("Hello");
        service.OnMessageSent("Second");
        service.OnMessageSent("Third");
        ev.Wait(TimeSpan.FromSeconds(2));

        Assert.Equal("Default Fallback", capturedName);
    }

    [Fact]
    public void BuildNamingPrompt_UsesCustomCursorPrompt()
    {
        var config = new AppConfig
        {
            ConversationNamingPrompt = "Custom prompt: {message}"
        };

        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Result");

        using var ev = new ManualResetEventSlim(false);
        using var service = new ConversationNamingService(mockLlm.Object, config);
        service.ConversationNameChanged += _ => ev.Set();

        service.OnMessageSent("test");
        service.OnMessageSent("test2");
        service.OnMessageSent("test3");
        ev.Wait(TimeSpan.FromSeconds(2));

        mockLlm.Verify(x => x.SendAsync(
            It.Is<string>(s => s.Contains("Custom prompt:") && s.Contains("User: test") && s.Contains("Current conversation")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
