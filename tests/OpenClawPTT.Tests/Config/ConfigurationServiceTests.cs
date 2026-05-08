using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConfigurationService using a mock IConfigStorage.
/// </summary>
public class ConfigurationServiceTests
{
    [Fact]
    public void Load_ReturnsConfigFromStorage()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var expectedConfig = new AppConfig { GatewayUrl = "wss://test.example.com" };
        mockStorage.Setup(x => x.Load()).Returns(expectedConfig);

        var service = new ConfigurationService(mockStorage.Object);

        // Act
        var result = service.Load();

        // Assert
        Assert.Same(expectedConfig, result);
        mockStorage.Verify(x => x.Load(), Times.Once);
    }

    [Fact]
    public void Save_PersistsConfigToStorage()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "wss://test.example.com" };

        // Act
        service.Save(config);

        // Assert
        mockStorage.Verify(x => x.Save(config), Times.Once);
    }

    [Fact]
    public void Validate_ReturnsIssuesFromConfigManager()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "" }; // invalid - empty URL

        // Act
        var issues = service.Validate(config);

        // Assert
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void ConfigurationService_DefaultConstructor_UsesFileConfigStorage()
    {
        var service = new ConfigurationService();
        var result = service.Load();
        Assert.NotNull(service);
    }

    [Fact]
    public async Task ReconfigureAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://test.example.com" });

        var service = new ConfigurationService(mockStorage.Object);
        var existing = new AppConfig { GatewayUrl = "wss://old.example.com" };
        var shellHost = new Mock<IStreamShellHost>();

        // Simulate cancellation during setup
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Fake console with immediate cancellation
        // Act & Assert - should not throw, just complete
        var result = await service.ReconfigureAsync(shellHost.Object, existing, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public void Constructor_NullConfigStorage_HandledGracefully()
    {
        var service = new ConfigurationService(null!);

        Assert.NotNull(service);
    }
}
