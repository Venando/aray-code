namespace ArayCode.Tests;

using ArayCode;
using ArayCode.Services;
using System;
using Xunit;
using Moq;

public class AppExitHandlerTests : IDisposable
{
    private readonly AppExitHandler _handler;

    public AppExitHandlerTests()
    {
        var mockConsole = new Mock<IColorConsole>().Object;
        _handler = new AppExitHandler(mockConsole);
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    #region ExitCancelled paths

    [Fact]
    public void HandleExit_NullException_ReturnsExitCancelled()
    {
        var result = _handler.HandleExit(null);
        Assert.Equal(AppExitHandler.ExitCancelled, result);
    }

    [Fact]
    public void HandleExit_OperationCanceledException_ReturnsExitCancelled()
    {
        var result = _handler.HandleExit(new OperationCanceledException());
        Assert.Equal(AppExitHandler.ExitCancelled, result);
    }

    #endregion

    #region ExitError paths

    [Fact]
    public void HandleExit_GatewayException_ReturnsExitError()
    {
        var gex = new GatewayException("connection refused");
        var result = _handler.HandleExit(gex);
        Assert.Equal(AppExitHandler.ExitError, result);
    }

    [Fact]
    public void HandleExit_GatewayException_DoesNotRethrow()
    {
        var gex = new GatewayException("boom");
        var thrown = Record.Exception(() => _handler.HandleExit(gex));
        Assert.Null(thrown);
    }

    [Fact]
    public void HandleExit_GenericException_ReturnsExitError()
    {
        var ex = new InvalidOperationException("something broke");
        var result = _handler.HandleExit(ex);
        Assert.Equal(AppExitHandler.ExitError, result);
    }

    [Fact]
    public void HandleExit_GenericException_DoesNotRethrow()
    {
        var ex = new Exception("boom");
        var thrown = Record.Exception(() => _handler.HandleExit(ex));
        Assert.Null(thrown);
    }

    #endregion
}
