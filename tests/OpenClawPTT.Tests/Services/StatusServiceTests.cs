using Xunit;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests;

public class StatusServiceTests
{
    [Fact]
    public void SetGatewayStatus_UpdatesRenderedText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);

        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("Connected", host.LastSeparatorRightText);
        Assert.Contains("green", host.LastSeparatorRightText);
    }

    [Fact]
    public void SetTtsStatus_UpdatesRenderedText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetTtsStatus("Disconnected", StatusColor.Red);

        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("Disconnected", host.LastSeparatorRightText);
        Assert.Contains("red", host.LastSeparatorRightText);
    }

    [Fact]
    public void MultipleUpdates_BothShown()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);
        service.SetTtsStatus("Starting", StatusColor.Yellow);

        Assert.Contains("Connected", host.LastSeparatorRightText);
        Assert.Contains("Starting", host.LastSeparatorRightText);
    }

    [Fact]
    public void ThreadSafe_ConcurrentCalls_NoCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        var t1 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetGatewayStatus("Status" + i, StatusColor.Green);
        });
        var t2 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetTtsStatus("Status" + i, StatusColor.Red);
        });

        var ex = Record.Exception(() => Task.WaitAll(t1, t2));
        Assert.Null(ex);
    }

    [Fact]
    public void DisposedHost_DoesNotCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);
        host.Dispose();

        var ex = Record.Exception(() => service.SetGatewayStatus("Test", StatusColor.Green));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_ThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() => new StatusService(null!));
    }
}
