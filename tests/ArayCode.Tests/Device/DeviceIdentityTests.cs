using Moq;
using ArayCode;
using Xunit;

namespace ArayCode.Tests;

public class DeviceIdentityTests : IDisposable
{
    private readonly string _testDir;
    public DeviceIdentityTests() { _testDir = Path.Combine(Path.GetTempPath(), $"oc_id_{Guid.NewGuid():N}"); }
    public void Dispose() { try { Directory.Delete(_testDir, recursive: true); } catch { } }

    private DeviceIdentity MakeDi() { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir); }
    private DeviceIdentity MakeDi(IPlatformInfo p) { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir, p); }

    [Fact] public void GetPlatform_Static_ReturnsPlatformString() {
        var p = DeviceIdentity.GetPlatform();
        Assert.NotNull(p);
        Assert.True(p == "windows" || p == "macos" || p == "linux");
    }

    [Fact] public void GetCurrentPlatform_WithMockPlatformInfo_ReturnsInjectedPlatform() {
        var m = new Mock<IPlatformInfo>(); m.Setup(x => x.GetPlatform()).Returns("freebsd");
        Assert.Equal("freebsd", MakeDi(m.Object).GetCurrentPlatform());
        m.Verify(x => x.GetPlatform(), Times.Once);
    }

    [Fact] public void Constructor_Default_UsesSystemPlatformInfo() {
        var p = MakeDi().GetCurrentPlatform();
        Assert.NotNull(p);
        Assert.True(p == "windows" || p == "macos" || p == "linux");
    }

    [Fact] public void EnsureKeypair_GeneratesKeypairSuccessfully() {
        var m = new Mock<IPlatformInfo>(); m.Setup(x => x.GetPlatform()).Returns("linux");
        var di = MakeDi(m.Object); di.EnsureKeypair();
        Assert.NotEmpty(di.DeviceId); Assert.NotEmpty(di.PublicKeyBase64);
        Assert.Equal(64, di.DeviceId.Length);
    }

    [Fact] public void Sign_ProducesNonEmptySignature() {
        var di = MakeDi(); di.EnsureKeypair();
        var s = di.Sign("test payload");
        Assert.NotEmpty(s); Assert.True(s.Length > 80);
    }

    [Fact] public void IPlatformInfo_CanBeMocked_ForTestIsolation() {
        var m = new Mock<IPlatformInfo>(); m.Setup(x => x.GetPlatform()).Returns("wasm");
        var di = MakeDi(m.Object); di.EnsureKeypair();
        Assert.Equal("wasm", di.GetCurrentPlatform());
    }
}
