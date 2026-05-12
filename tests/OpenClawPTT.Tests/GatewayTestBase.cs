// Implementation Plan: TestFixtureBuilder/TestBase for OpenClaw PTT Tests
// This file serves as both documentation and the actual implementation

#nullable enable
using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests;

/// <summary>
/// Centralized test fixture builder for Gateway-related tests.
/// Use this to create consistently configured test dependencies.
/// </summary>
/// <remarks>
/// DESIGN DECISION: Builder Pattern vs Base Class
/// 
/// We chose a HYBRID approach:
/// 1. TestFixtureBuilder (static class) - For creating reusable mock configurations
/// 2. GatewayTestBase (abstract class) - For tests needing shared setup/teardown
///
/// Why not pure Base Class?
/// - xUnit prefers clean, isolated test classes
/// - Some tests need different setup (e.g., GatewayServiceStabilityTests uses TestableGatewayService)
/// - Builder pattern allows composition over inheritance
///
/// Why not pure Builder?
/// - Common setup code still benefits from shared initialization
/// - IDisposable pattern is cleaner in a base class
/// </remarks>
public static class TestFixtureBuilder
{
    /// <summary>
    /// Default test configuration values
    /// </summary>
    public static class Defaults
    {
        public const string GatewayUrl = "wss://test.example.com";
        public const string AuthToken = "test-token";
        public const string DeviceToken = "test-device-token";
        public const string DataDir = "/tmp/openclaw-test";
    }

    /// <summary>
    /// Creates a standard AppConfig for testing with sensible defaults.
    /// </summary>
    public static AppConfig CreateConfig(Action<AppConfig>? configure = null)
    {
        var config = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = Defaults.GatewayUrl,
            AuthToken = Defaults.AuthToken,
            DeviceToken = Defaults.DeviceToken,
        };

        configure?.Invoke(config);
        return config;
    }

    /// <summary>
    /// Creates a DeviceIdentity with guaranteed keypair for testing.
    /// </summary>
    public static DeviceIdentity CreateDeviceIdentity(string? dataDir = null)
    {
        var dir = dataDir ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        var device = new DeviceIdentity(dir);
        device.EnsureKeypair();
        return device;
    }

    /// <summary>
    /// Creates a fresh GatewayEventSource for testing.
    /// </summary>
    public static GatewayEventSource CreateEventSource() => new();

    /// <summary>
    /// Creates a mock IGatewayConnectionLifecycle with standard setups.
    /// </summary>
    public static Mock<IGatewayConnectionLifecycle> CreateMockLifecycle(
        bool isConnected = false,
        IMessageFraming? framing = null)
    {
        var mock = new Mock<IGatewayConnectionLifecycle>();
        mock.Setup(x => x.IsConnected).Returns(isConnected);

        if (framing != null)
        {
            mock.Setup(x => x.GetFraming()).Returns(framing);
        }

        return mock;
    }

    /// <summary>
    /// Creates a mock IClientWebSocket with specified state.
    /// </summary>
    public static Mock<IClientWebSocket> CreateMockWebSocket(
        System.Net.WebSockets.WebSocketState state = System.Net.WebSockets.WebSocketState.Open)
    {
        var mock = new Mock<IClientWebSocket>();
        mock.Setup(x => x.State).Returns(state);
        return mock;
    }

    /// <summary>
    /// Creates a mock IMessageFraming with standard behavior.
    /// </summary>
    public static Mock<IMessageFraming> CreateMockFraming()
    {
        var mock = new Mock<IMessageFraming>();
        return mock;
    }

    /// <summary>
    /// Creates a mock IGatewayClient with standard setups.
    /// </summary>
    public static Mock<IGatewayClient> CreateMockGatewayClient()
    {
        var mock = new Mock<IGatewayClient>();
        mock.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(System.Text.Json.JsonElement));
        return mock;
    }

    /// <summary>
    /// Complete fixture context for Gateway tests.
    /// Use this when you need all dependencies together.
    /// </summary>
    public class GatewayFixture : IDisposable
    {
        public AppConfig Config { get; }
        public DeviceIdentity Device { get; }
        public GatewayEventSource EventSource { get; }
        public Mock<IGatewayConnectionLifecycle> MockLifecycle { get; }
        public string TempDirectory { get; }

        public GatewayFixture(
            Action<AppConfig>? configureConfig = null,
            bool lifecycleConnected = false)
        {
            TempDirectory = Path.Combine(Path.GetTempPath(), $"openclaw-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(TempDirectory);

            Config = CreateConfig(c =>
            {
                c.CustomDataDir = TempDirectory;
                configureConfig?.Invoke(c);
            });

            Device = CreateDeviceIdentity(TempDirectory);
            EventSource = CreateEventSource();
            MockLifecycle = CreateMockLifecycle(isConnected: lifecycleConnected);
        }

        /// <summary>
        /// Creates a GatewayClient using this fixture's dependencies.
        /// </summary>
        public GatewayClient CreateClient() =>
            new(Config, Device, EventSource, new Mock<IColorConsole>().Object, () => MockLifecycle.Object);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                {
                    Directory.Delete(TempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}

/// <summary>
/// Base class for Gateway-related tests that need shared setup.
/// Provides automatic cleanup and common test dependencies.
/// </summary>
/// <remarks>
/// Inherit from this when:
/// - Your tests need consistent AppConfig/DeviceIdentity/EventSource setup
/// - You want automatic temp directory cleanup
/// - You're testing GatewayClient or GatewayConnectionLifecycle directly
///
/// Don't inherit when:
/// - You need a custom test double (like TestableGatewayService)
/// - You have specialized setup requirements
/// - Use TestFixtureBuilder directly instead
/// </remarks>
public abstract class GatewayTestBase : IDisposable
{
    protected string TempDirectory { get; }
    protected AppConfig Config { get; }
    protected DeviceIdentity Device { get; }
    protected GatewayEventSource EventSource { get; }

    protected GatewayTestBase(Action<AppConfig>? configureConfig = null)
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), $"openclaw-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(TempDirectory);

        Config = TestFixtureBuilder.CreateConfig(c =>
        {
            c.CustomDataDir = TempDirectory;
            configureConfig?.Invoke(c);
        });

        Device = TestFixtureBuilder.CreateDeviceIdentity(TempDirectory);
        EventSource = TestFixtureBuilder.CreateEventSource();
    }

    /// <summary>
    /// Creates a mock lifecycle with optional connected state.
    /// </summary>
    protected Mock<IGatewayConnectionLifecycle> CreateMockLifecycle(bool isConnected = false) =>
        TestFixtureBuilder.CreateMockLifecycle(isConnected);

    /// <summary>
    /// Creates a GatewayClient with the base fixture's dependencies.
    /// </summary>
    protected GatewayClient CreateClient(Func<IGatewayConnectionLifecycle>? lifecycleFactory = null) =>
        new(Config, Device, EventSource, new Mock<IColorConsole>().Object, lifecycleFactory ?? (() => CreateMockLifecycle().Object));

    public virtual void Dispose()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
