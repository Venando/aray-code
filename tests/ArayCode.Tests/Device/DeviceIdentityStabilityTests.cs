using System.Diagnostics;
using Xunit;

namespace ArayCode.Tests;

public class DeviceIdentityStabilityTests : IDisposable
{
    private readonly string _testDir;
    public DeviceIdentityStabilityTests() { _testDir = Path.Combine(Path.GetTempPath(), $"oc_stability_{Guid.NewGuid():N}"); }
    public void Dispose() { try { Directory.Delete(_testDir, recursive: true); } catch { } }
    private DeviceIdentity MakeDi() { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir); }

    [Fact]
    public void Sign_BeforeEnsureKeypair_ThrowsInvalidOperationException()
    {
        var di = MakeDi();
        var ex = Assert.Throws<InvalidOperationException>(() => di.Sign("any payload"));
        Assert.Contains("EnsureKeypair", ex.Message);
    }

    [Fact]
    public void EnsureKeypair_ReadOnlyDirectory_ThrowsIoOrUnauthorized()
    {
        if (OperatingSystem.IsWindows()) return;
        var fakeDataDir = Path.Combine("/dev", $"oc_test_{Guid.NewGuid():N}");
        var di = new DeviceIdentity(fakeDataDir);
        try { di.EnsureKeypair(); Assert.Fail("Expected exception"); }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    [Fact]
    public void EnsureKeypair_ExistingFileReadOnly_ThrowsIoOrUnauthorized()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var keyFile = Path.Combine(_testDir, "device.key");
        if (OperatingSystem.IsWindows())
        {
            File.Delete(keyFile); using (File.Create(keyFile)) { }
            File.SetAttributes(keyFile, FileAttributes.ReadOnly);
            try { try { di.EnsureKeypair(); Assert.Fail("Expected"); } catch (UnauthorizedAccessException) { } catch (IOException) { } catch (ArgumentException) { } }
            finally { File.SetAttributes(keyFile, FileAttributes.Normal); }
        }
        else
        {
            File.Delete(keyFile); using (File.Create(keyFile)) { }
            Bash($"chmod a-w \"{keyFile}\"");
            try { try { di.EnsureKeypair(); Assert.Fail("Expected"); } catch (UnauthorizedAccessException) { } catch (IOException) { } catch (ArgumentException) { } }
            finally { Bash($"chmod u+w \"{keyFile}\""); }
        }
    }

    [Fact]
    public void EnsureKeypair_WriteFailure_DoesNotCorruptExistingKey()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var origPub = di.PublicKeyBase64; var origId = di.DeviceId;
        var origContent = File.ReadAllText(Path.Combine(_testDir, "device.key"));
        di.EnsureKeypair();
        Assert.Equal(origPub, di.PublicKeyBase64); Assert.Equal(origId, di.DeviceId);
        Assert.Equal(origContent, File.ReadAllText(Path.Combine(_testDir, "device.key")));
    }

    [Fact]
    public void Sign_LargePayload_StillWorks()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var sig = di.Sign(new string('x', 1_200_000));
        Assert.NotEmpty(sig); Assert.Equal(64, DeviceTestHelpers.FromBase64Url(sig).Length);
    }

    [Fact]
    public void Sign_HugePayload_5MB_StillWorks()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var sig = di.Sign(new string('a', 5 * 1024 * 1024));
        Assert.NotEmpty(sig); Assert.Equal(64, DeviceTestHelpers.FromBase64Url(sig).Length);
    }

    [Fact]
    public void BuildV3Payload_NullScope_ThrowsArgumentNullException()
    {
        var di = MakeDi(); di.EnsureKeypair();
        IEnumerable<string>? ns = null;
        Assert.Throws<ArgumentNullException>(() => di.BuildV3Payload("linux", "desktop", "cid", "ptt", "user", ns!, 1000L, "tok", "non"));
    }

    [Fact]
    public void BuildV3Payload_EmptyScope_Works()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var payload = di.BuildV3Payload("linux", "desktop", "cid", "ptt", "user", Array.Empty<string>(), 1000L, "tok", "non");
        Assert.Contains("v3|", payload); Assert.Equal("", payload.Split('|')[5]);
    }

    [Fact]
    public void BuildV3Payload_ScopeWithCommas_JoinedWithComma()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var payload = di.BuildV3Payload("linux", "desktop", "cid", "ptt", "user", new[] { "gateway.connect,admin", "audio.record" }, 1000L, "tok", "non");
        Assert.Equal("gateway.connect,admin,audio.record", payload.Split('|')[5]);
    }

    [Fact]
    public void BuildV3Payload_ScopeWithMultipleCommas_JoinedCorrectly()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var payload = di.BuildV3Payload("linux", "desktop", "cid", "ptt", "user", new[] { "a,b,c", "d,e,f", "g" }, 1000L, "tok", "non");
        Assert.Equal("a,b,c,d,e,f,g", payload.Split('|')[5]);
    }

    private static void Bash(string cmd)
    {
        var psi = new ProcessStartInfo { FileName = "/bin/bash", Arguments = $"-c \"{cmd}\"", UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi); p?.WaitForExit();
    }
}
