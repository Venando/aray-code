using Xunit;

namespace OpenClawPTT.Tests;

public class DeviceIdentityEdgeCaseTests : IDisposable
{
    private readonly string _testDir;
    public DeviceIdentityEdgeCaseTests() { _testDir = Path.Combine(Path.GetTempPath(), $"oc_deveid_{Guid.NewGuid():N}"); }
    public void Dispose() { try { Directory.Delete(_testDir, recursive: true); } catch { } }
    private DeviceIdentity MakeDi() { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir); }

    [Fact] public void EnsureKeypair_CreatesKeysIfMissing() {
        var di = MakeDi(); di.EnsureKeypair();
        Assert.True(File.Exists(Path.Combine(_testDir, "device.key")));
        Assert.NotEmpty(di.DeviceId); Assert.NotEmpty(di.PublicKeyBase64);
    }

    [Fact] public void EnsureKeypair_ReusesExistingKey_NoNewKeyGenerated() {
        var di1 = MakeDi(); di1.EnsureKeypair();
        var k1 = di1.PublicKeyBase64; var id1 = di1.DeviceId;
        var fc = File.ReadAllText(Path.Combine(_testDir, "device.key"));
        var di2 = MakeDi(); di2.EnsureKeypair();
        Assert.Equal(k1, di2.PublicKeyBase64); Assert.Equal(id1, di2.DeviceId);
        Assert.Equal(fc, File.ReadAllText(Path.Combine(_testDir, "device.key")));
    }

    [Fact] public void EnsureKeypair_CanBeCalledMultipleTimes() {
        var di = MakeDi(); di.EnsureKeypair(); di.EnsureKeypair(); di.EnsureKeypair();
        Assert.NotEmpty(di.DeviceId); Assert.NotEmpty(di.PublicKeyBase64);
    }

    [Fact] public void EnsureKeypair_DifferentInstances_SameDir_SameIdentity() {
        var d1 = MakeDi(); d1.EnsureKeypair(); var d2 = MakeDi(); d2.EnsureKeypair(); var d3 = MakeDi(); d3.EnsureKeypair();
        Assert.Equal(d1.DeviceId, d2.DeviceId); Assert.Equal(d2.DeviceId, d3.DeviceId);
        Assert.Equal(d1.PublicKeyBase64, d2.PublicKeyBase64); Assert.Equal(d2.PublicKeyBase64, d3.PublicKeyBase64);
    }

    [Fact] public void Keys_StoredInDataDir_NotElsewhere() {
        var di = MakeDi(); di.EnsureKeypair();
        var kf = Path.Combine(_testDir, "device.key"); Assert.True(File.Exists(kf));
        Assert.Single(Directory.GetFiles(_testDir, "*.key", SearchOption.AllDirectories), kf);
    }

    [Fact] public void Sign_SamePayload_ProducesSameSignature() {
        var di = MakeDi(); di.EnsureKeypair();
        var p = "v3|deviceid|clientid|clientmode|role|scope1,scope2|1234567890|token|nonce|platform|family";
        Assert.Equal(di.Sign(p), di.Sign(p));
        Assert.Equal(di.Sign(p), di.Sign(p));
    }

    [Fact] public void Sign_DifferentPayloads_ProduceDifferentSignatures() {
        var di = MakeDi(); di.EnsureKeypair();
        var s1 = di.Sign("payload one"); var s2 = di.Sign("payload two"); var s3 = di.Sign("");
        Assert.NotEqual(s1, s2); Assert.NotEqual(s2, s3); Assert.NotEqual(s1, s3);
    }

    [Fact] public void Sign_OutputIsBase64UrlEncoded() {
        var di = MakeDi(); di.EnsureKeypair(); var s = di.Sign("test");
        Assert.DoesNotContain("+", s); Assert.DoesNotContain("/", s); Assert.False(s.EndsWith("="));
    }

    [Fact] public void Sign_SignatureIs64Bytes() {
        var di = MakeDi(); di.EnsureKeypair();
        Assert.Equal(64, DeviceTestHelpers.FromBase64Url(di.Sign("test")).Length);
    }

    [Fact] public void DeviceId_IsLowercaseHex() {
        var di = MakeDi(); di.EnsureKeypair(); Assert.Matches("^[a-f0-9]{64}$", di.DeviceId);
    }

    [Fact] public void DeviceId_IsSHA256OfPublicKey() {
        var di = MakeDi(); di.EnsureKeypair();
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(DeviceTestHelpers.FromBase64Url(di.PublicKeyBase64))).ToLowerInvariant();
        Assert.Equal(expected, di.DeviceId);
    }

    [Fact] public void EnsureKeypair_CorruptKeyFile_ThrowsFormatException() {
        var di = MakeDi(); File.WriteAllText(Path.Combine(_testDir, "device.key"), "not-valid-base64!!!");
        Assert.Throws<System.FormatException>(() => di.EnsureKeypair());
    }

    [Fact] public void EnsureKeypair_TruncatedKeyFile_Throws() {
        var di = MakeDi();
        File.WriteAllText(Path.Combine(_testDir, "device.key"), Convert.ToBase64String(new byte[16]));
        Assert.ThrowsAny<Exception>(() => di.EnsureKeypair());
    }

    [Fact] public void EnsureKeypair_EmptyKeyFile_Throws() {
        var di = MakeDi(); File.WriteAllText(Path.Combine(_testDir, "device.key"), "");
        Assert.ThrowsAny<Exception>(() => di.EnsureKeypair());
    }

    [Fact] public void BuildV3Payload_FormatsCorrectly() {
        var di = MakeDi(); di.EnsureKeypair();
        Assert.Equal($"v3|{di.DeviceId}|client-abc|ptt|user|gateway.connect,audio.record|1234567890000|tok|non|linux|desktop",
            di.BuildV3Payload("linux","desktop","client-abc","ptt","user",new[]{"gateway.connect","audio.record"},1234567890000L,"tok","non"));
    }

    [Fact] public void BuildV3Payload_SingleScope_Works() {
        var di = MakeDi(); di.EnsureKeypair();
        var p = di.BuildV3Payload("linux","desktop","c","ptt","user",new[]{"only-one"},1000L,"t","n");
        Assert.Contains("only-one", p); Assert.DoesNotContain(",", p.Split('|')[5]);
    }
}
