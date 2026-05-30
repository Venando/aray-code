using Xunit;
using Moq;
using ArayCode.Services;
using ArayCode;
using ArayCode.Services.StatusParts;
using ArayCode.Services.Themes;

namespace ArayCode.Tests;

[Collection("AgentRegistryCollection")]
public class StatusServiceTests
{
    static StatusServiceTests()
    {
        AgentSettingsPersistenceLegacy.Initialize(Mock.Of<IAgentSettingsPersistence>());
        // Use a stable test theme so color assertions are deterministic
        ThemeProvider.Current = new ThemeConfig
        {
            Tools = new ToolTheme
            {
                Messages = new MessageStyles
                {
                    Success = "green",
                    Warning = "yellow",
                    Error = "red",
                    Info = "grey"
                }
            }
        };
    }

    [Fact]
    public void SetServiceStatus_Gateway_ShowsGreenDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);

        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("[" + ThemeProvider.Current.Tools.Messages.Success + "]", host.LastSeparatorRightText);
        Assert.Contains("●", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void SetServiceStatus_Tts_ShowsRedDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);

        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("[" + ThemeProvider.Current.Tools.Messages.Error + "]", host.LastSeparatorRightText);
        Assert.Contains("●", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void MultipleUpdates_DotsShown()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
        service.SetServiceStatus(ServiceKind.Tts, StatusColor.Yellow);

        // Should show labels + green dot + yellow animating dot
        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("[" + ThemeProvider.Current.Tools.Messages.Success + "]", host.LastSeparatorRightText);
        Assert.Contains("[" + ThemeProvider.Current.Tools.Messages.Warning + "]", host.LastSeparatorRightText);
        // Yellow dot animates — first frame is '•'
        Assert.Contains("•", host.LastSeparatorRightText); // •
    }

    [Fact]
    public void SetServiceStatus_ShowsLlmDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Green);

        Assert.Contains("LLM:", host.LastSeparatorRightText);
        Assert.Contains("[" + ThemeProvider.Current.Tools.Messages.Success + "]", host.LastSeparatorRightText);
        Assert.Contains("●", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public async Task ThreadSafe_ConcurrentCalls_NoCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        var t1 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
        });
        var t2 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
        });

        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public void DisposedHost_DoesNotCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);
        host.Dispose();

        service.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
    }

    [Fact]
    public void Constructor_ThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() => new StatusService(null!));
    }
}