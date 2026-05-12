using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
using Xunit;
using CommandType = OpenClawPTT.Services.Commands.CommandType;

namespace OpenClawPTT.Tests.Services;

public class ConversationNamingServiceTests : IDisposable
{
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
        var service = new ConversationNamingService(mockLlm.Object);

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

        var service = new ConversationNamingService(mockLlm.Object);
        string? capturedName = null;
        service.ConversationNameChanged += name => capturedName = name;

        service.OnMessageSent("Can you review this C# code for me?");
        Thread.Sleep(200);

        Assert.Equal("Code Review", capturedName);
        Assert.Equal("Code Review", service.GetCurrentConversationName());
    }

    [Fact]
    public void OnMessageSent_DoesNothing_WhenDirectLlmNotConfigured()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(false);

        var service = new ConversationNamingService(mockLlm.Object);
        bool eventFired = false;
        service.ConversationNameChanged += _ => eventFired = true;

        service.OnMessageSent("Any message");
        Thread.Sleep(200);

        Assert.False(eventFired);
        Assert.Null(service.GetCurrentConversationName());
    }

    [Fact]
    public void OnMessageSent_OnlyGeneratesNameOnce_PerSession()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        var service = new ConversationNamingService(mockLlm.Object);

        service.OnMessageSent("First message");
        service.OnMessageSent("Second message");
        Thread.Sleep(200);

        mockLlm.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SetConversationName_SanitizesName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("\"Name:\" \"Code Review\"");

        var service = new ConversationNamingService(mockLlm.Object);
        string? capturedName = null;
        service.ConversationNameChanged += name => capturedName = name;

        service.OnMessageSent("Message");
        Thread.Sleep(200);

        Assert.Equal("Code Review", capturedName);
    }

    [Fact]
    public void OnCommandExecuted_Reset_ClearsConversationName()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Name");

        var service = new ConversationNamingService(mockLlm.Object);
        string? capturedName = "initial";
        service.ConversationNameChanged += name => capturedName = name;

        service.OnMessageSent("Message");
        Thread.Sleep(200);
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "reset", CommandSource.OpenClaw, CommandType.SessionControl,
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

        var service = new ConversationNamingService(mockLlm.Object);
        service.OnMessageSent("Message");
        Thread.Sleep(200);
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "new", CommandSource.OpenClaw, CommandType.SessionControl,
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

        var service = new ConversationNamingService(mockLlm.Object);
        service.OnMessageSent("Message");
        Thread.Sleep(200);
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "config", CommandSource.OpenClaw, CommandType.Admin,
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

        var service = new ConversationNamingService(mockLlm.Object);
        service.OnMessageSent("Message");
        Thread.Sleep(200);

        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "RESET", CommandSource.OpenClaw, CommandType.SessionControl,
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

        var service = new ConversationNamingService(mockLlm.Object);
        service.OnMessageSent("Message");
        Thread.Sleep(200);
        Assert.Equal("Test Name", service.GetCurrentConversationName());

        // A native "reset" command that is NOT SessionControl should not clear
        service.OnCommandExecuted(null, new CommandExecutedEventArgs(
            "reset", CommandSource.Native, CommandType.Unknown,
            Array.Empty<string>(), new Dictionary<string, string>()));

        // Still named because type check fails
        Assert.Equal("Test Name", service.GetCurrentConversationName());
    }
}
