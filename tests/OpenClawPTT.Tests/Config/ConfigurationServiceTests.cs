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
        Assert.NotNull(service);
    }

    [Fact]
    public void Save_FiresConfigSavedEvent()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns((AppConfig?)null);
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "wss://test.example.com" };
        bool fired = false;

        service.ConfigSaved += args =>
        {
            fired = true;
            Assert.Same(config, args.NewConfig);
        };

        // Act
        service.Save(config);

        // Assert
        Assert.True(fired);
    }

    [Fact]
    public void Save_FiresConfigValidatingEvent()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns((AppConfig?)null);
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "wss://test.example.com" };
        bool fired = false;

        service.ConfigValidating += args =>
        {
            fired = true;
            Assert.Same(config, args.NewConfig);
        };

        // Act
        service.Save(config);

        // Assert
        Assert.True(fired);
    }

    [Fact]
    public void Constructor_NullConfigStorage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigurationService(null!));
    }
}
