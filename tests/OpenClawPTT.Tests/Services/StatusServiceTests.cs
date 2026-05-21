using Xunit;
using Moq;
using OpenClawPTT.Services;
using OpenClawPTT;
using OpenClawPTT.Services.StatusParts;

namespace OpenClawPTT.Tests;

[Collection("AgentRegistryCollection")]
public class StatusServiceTests
{
    static StatusServiceTests()
    {
        AgentSettingsPersistenceLegacy.Initialize(Mock.Of<IAgentSettingsPersistence>());
    }

    [Fact]
    public void SetServiceStatus_Gateway_ShowsGreenDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);

        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void SetServiceStatus_Tts_ShowsRedDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);

        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("[red]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
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
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("[yellow]", host.LastSeparatorRightText);
        // Yellow dot animates — first frame is '•'
        Assert.Contains("\u2022", host.LastSeparatorRightText); // •
    }

    [Fact]
    public void SetServiceStatus_ShowsLlmDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Green);

        Assert.Contains("LLM:", host.LastSeparatorRightText);
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
    }



    [Fact]
    public void ThreadSafe_ConcurrentCalls_NoCrash()
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

        Task.WaitAll(t1, t2);
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
