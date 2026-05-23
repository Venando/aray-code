using Moq;
using ArayCode;
using ArayCode.Services;
using ArayCode.TTS;
using ArayCode.TTS.Providers;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Tests for TtsService lifecycle and error handling.
/// </summary>
public class TtsServiceTests : IDisposable
{
    private readonly Mock<IColorConsole> _mockConsole = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static AppConfig PiperConfig(string piperPath, string modelPath = "")
        => new()
        {
            TtsProvider = TtsProviderType.Piper,
            PiperPath = piperPath,
            PiperModelPath = modelPath,
            PiperVoice = "en_US-lessac",
        };

    [Fact]
    public void TtsService_Piper_BinaryMissing_NoCrash()
    {
        var config = PiperConfig(piperPath: "/also/does/not/exist/piper");

        var service = new TtsService(config, _mockConsole.Object);

        Assert.NotNull(service);
        Assert.Equal(TtsProviderType.Piper, service.ProviderType);
        Assert.True(service.IsConfigured);

        service.Dispose();
    }

    [Fact]
    public void TtsService_Dispose_WhileInitializing_NoCrash()
    {
        var config = PiperConfig(piperPath: "/impossible/path/to/piper");

        TtsService? service = null;
        try { service = new TtsService(config, _mockConsole.Object); }
        catch { /* piper might throw */ }

        if (service != null)
        {
            var disposeEx = Record.Exception(() => service.Dispose());
            Assert.Null(disposeEx);
            service.Dispose();
        }
    }

    [Fact]
    public void TtsService_EdgeProvider_NoSubscriptionKey_Graceful()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = null,
            TtsRegion = "eastus",
        };

        var service = new TtsService(config, _mockConsole.Object);

        Assert.False(service.IsConfigured);
        Assert.Equal(TtsProviderType.Edge, service.ProviderType);
    }

    [Fact]
    public void TtsService_Dispose_Idempotent()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = "fake-key",
        };

        var service = new TtsService(config, _mockConsole.Object);

        var firstEx = Record.Exception(() => service.Dispose());
        Assert.Null(firstEx);

        var secondEx = Record.Exception(() => service.Dispose());
        Assert.Null(secondEx);
    }

    [Fact]
    public void TtsService_OpenAI_NoApiKey_ThrowsClearError()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.OpenAI,
            TtsOpenAiApiKey = null,
            OpenAiApiKey = null,
        };

        var ex = Record.Exception(() => new TtsService(config, _mockConsole.Object));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("OpenAI", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TtsService_EdgeProviderWithKey_ProviderIsSet()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = "fake-key-for-test",
            TtsRegion = "eastus",
        };

        var service = new TtsService(config, _mockConsole.Object);

        Assert.True(service.IsConfigured);
        Assert.NotNull(service.Provider);
        Assert.Equal(TtsProviderType.Edge, service.ProviderType);
        service.Dispose();
    }

    [Fact]
    public void PiperTtsProvider_MissingBinary_SynthesizeAsync_ThrowsClearError()
    {
        var provider = new PiperTtsProvider(
            piperPath: "/nonexistent/piper",
            modelPath: "",
            voice: "en_US-lessac");

        var ex = Record.Exception(() => provider.SynthesizeAsync("test").GetAwaiter().GetResult());

        Assert.IsType<FileNotFoundException>(ex);
        Assert.Contains("piper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
    }
}
