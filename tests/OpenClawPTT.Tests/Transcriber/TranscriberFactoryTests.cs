using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using System;
using Xunit;

namespace OpenClawPTT.Tests;

public class TranscriberFactoryTests
{
    private readonly Mock<IColorConsole> _mockConsole = new();
    private readonly Mock<IStreamShellHost> _mockShellHost = new();
    private IColorConsole Console => _mockConsole.Object;

    public TranscriberFactoryTests()
    {
        _mockConsole.Setup(c => c.GetStreamShellHost()).Returns(_mockShellHost.Object);
    }

    #region Null API key scenarios

    [Fact]
    public void Create_OpenAiProvider_NullApiKey_ThrowsInvalidOperationException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = null
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg, Console));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Create_WhisperCppProvider_NullModel_DoesNotThrow()
    {
        // whisper-cpp doesn't use an API key, model is optional (defaults to "base")
        var cfg = new AppConfig
        {
            SttProvider = "whisper-cpp",
            WhisperCppModel = null
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg, Console));

        // Should not throw — whisper-cpp uses model manager based config
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_OpenAiProvider_InvalidApiKey_Throws(string apiKey)
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = apiKey
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg, Console));

        Assert.True(ex is InvalidOperationException or ArgumentException);
    }

    #endregion

    #region Unknown / invalid provider scenarios

    [Fact]
    public void Create_UnknownProvider_ThrowsArgumentException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "unknown-provider"
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg, Console));

        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("Unknown STT provider", ex.Message);
    }

    #endregion

    #region Default provider scenarios

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_NullOrEmptyProvider_FallsBackToGroq(string? sttProvider)
    {
        var cfg = new AppConfig
        {
            SttProvider = sttProvider,
            GroqApiKey = "gsk_testkey"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        Assert.IsType<GroqTranscriberAdapter>(result);
    }

    #endregion

    #region Null config scenario

    [Fact]
    public void Create_NullConfig_ThrowsNullReferenceException()
    {
        var ex = Record.Exception(() => TranscriberFactory.Create(null!, Console));

        Assert.IsType<NullReferenceException>(ex);
    }

    #endregion

    #region Valid provider instantiations

    [Fact]
    public void Create_OpenAiProvider_ValidKey_ReturnsOpenAiTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        Assert.IsType<OpenAiTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_WhisperCppProvider_ValidModel_ReturnsWhisperCppTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "whisper-cpp",
            WhisperCppModel = "base"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        Assert.IsType<WhisperCppTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_GroqProvider_ReturnsGroqTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "groq",
            GroqApiKey = "gsk_testkey"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        Assert.IsType<GroqTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_OpenAiProvider_CaseInsensitive()
    {
        var cfg = new AppConfig
        {
            SttProvider = "OPENAI",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        Assert.IsType<OpenAiTranscriberAdapter>(result);
    }

    #endregion

    #region Disposal

    [Fact]
    public void Create_ReturnsDisposable_Adapter_CanBeDisposed()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg, Console);

        var disposable = Assert.IsAssignableFrom<IDisposable>(result);
        disposable.Dispose();
    }

    #endregion
}
