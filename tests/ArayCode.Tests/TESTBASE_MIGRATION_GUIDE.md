# TestFixtureBuilder/TestBase Implementation Plan

## Overview

This document describes the implementation plan for reducing test setup duplication across Gateway tests.

## Problem Statement

Three test files have significant setup duplication:
- `GatewayClientTests.cs` - 110 lines of repeated config/device/event setup
- `GatewayConnectionLifecycleTests.cs` - Constructor with repeated setup + 20+ tests
- `GatewayServiceStabilityTests.cs` - Separate config setup pattern

### Duplicated Patterns Identified

```csharp
// Pattern 1: AppConfig creation (appears in ~20 test methods)
var cfg = new AppConfig {
    CustomDataDir = Path.GetTempPath(),
    GatewayUrl = "wss://test",
    AuthToken = "test"
};

// Pattern 2: DeviceIdentity setup (appears in ~20 test methods)
var dev = new DeviceIdentity(cfg.DataDir);
dev.EnsureKeypair();

// Pattern 3: EventSource creation (appears in ~20 test methods)
var events = new GatewayEventSource();

// Pattern 4: Mock lifecycle setup
var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
```

## Solution: Hybrid Approach

We provide both a **Builder Pattern** (composition) and a **Base Class** (inheritance):

### 1. TestFixtureBuilder (Static Class)

For lightweight, explicit setup without inheritance:

```csharp
// Quick one-off fixture creation
using var fixture = new TestFixtureBuilder.GatewayFixture();
var client = fixture.CreateClient();
```

### 2. GatewayTestBase (Abstract Class)

For test classes needing consistent shared state:

```csharp
public class MyGatewayTests : GatewayTestBase
{
    public MyGatewayTests() : base(cfg => {
        cfg.GatewayUrl = "wss://special";
    }) { }

    [Fact]
    public void Test() {
        // Access Config, Device, EventSource directly
        var client = CreateClient();
    }
}
```

## File Structure

```
tests/OpenClawPTT.Tests/
├── GatewayTestBase.cs              # Core implementation
├── Gateway/                        # Example refactored tests
│   ├── GatewayClientTests_Refactored.cs
│   └── GatewayConnectionLifecycleTests_Refactored.cs
└── TESTBASE_MIGRATION_GUIDE.md     # This document
```

## API Reference

### TestFixtureBuilder

| Method | Purpose |
|--------|---------|
| `CreateConfig(Action<AppConfig>? configure)` | Create AppConfig with defaults |
| `CreateDeviceIdentity(string? dataDir)` | Create DeviceIdentity with keypair |
| `CreateEventSource()` | Create fresh GatewayEventSource |
| `CreateMockLifecycle(bool isConnected, IMessageFraming?)` | Create mock lifecycle |
| `CreateMockWebSocket(WebSocketState)` | Create mock websocket |
| `CreateMockFraming()` | Create mock message framing |
| `CreateMockGatewayClient()` | Create mock gateway client |

### GatewayFixture (IDisposable)

| Property | Description |
|----------|-------------|
| `Config` | Pre-configured AppConfig |
| `Device` | DeviceIdentity with keypair |
| `EventSource` | Fresh GatewayEventSource |
| `MockLifecycle` | Mock<IGatewayConnectionLifecycle> |
| `TempDirectory` | Auto-created temp folder |

| Method | Description |
|--------|-------------|
| `CreateClient()` | Build GatewayClient from fixture |

### GatewayTestBase (Abstract)

| Property | Description |
|----------|-------------|
| `Config` | Pre-configured AppConfig |
| `Device` | DeviceIdentity with keypair |
| `EventSource` | Fresh GatewayEventSource |
| `TempDirectory` | Auto-created temp folder |

| Method | Description |
|--------|-------------|
| `CreateMockLifecycle(bool)` | Create mock lifecycle |
| `CreateClient(Func<IGatewayConnectionLifecycle>? factory)` | Build GatewayClient |

## Migration Strategy

### Phase 1: Add New Infrastructure (No Breaking Changes)

1. Add `GatewayTestBase.cs` to test project
2. Verify compiles alongside existing tests
3. Run all existing tests to ensure no regressions

### Phase 2: Migrate GatewayClientTests

**Before:**
```csharp
[Fact]
public void SomeTest()
{
    var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
    var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), ... };
    var dev = new DeviceIdentity(cfg.DataDir);
    dev.EnsureKeypair();
    var events = new GatewayEventSource();
    var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
    // ... test code
    client.Dispose();
}
```

**After (Builder Pattern):**
```csharp
[Fact]
public void SomeTest()
{
    using var fixture = new TestFixtureBuilder.GatewayFixture();
    var client = fixture.CreateClient();
    // ... test code
}
```

**After (Base Class):**
```csharp
public class GatewayClientTests : GatewayTestBase
{
    [Fact]
    public void SomeTest()
    {
        var client = CreateClient();
        // ... test code
    }
}
```

### Phase 3: Migrate GatewayConnectionLifecycleTests

This class has a constructor with shared setup - good candidate for base class:

```csharp
public class GatewayConnectionLifecycleTests : GatewayTestBase
{
    private readonly Mock<IClientWebSocket> _mockWs;

    public GatewayConnectionLifecycleTests()
    {
        Config.GatewayUrl = "wss://127.0.0.1:9999/test";
        Config.AuthToken = "test-token";
        _mockWs = TestFixtureBuilder.CreateMockWebSocket(WebSocketState.Open);
    }

    // Tests use inherited Config, Device, EventSource
}
```

### Phase 4: Migrate GatewayServiceStabilityTests

This uses `TestableGatewayService` - needs special handling:

```csharp
public class GatewayServiceStabilityTests : IDisposable
{
    // Keep existing structure, use builder for config
    private readonly AppConfig _config;

    public GatewayServiceStabilityTests()
    {
        _config = TestFixtureBuilder.CreateConfig();
    }
    // ... rest of tests unchanged
}
```

## Decision Matrix: When to Use What

| Scenario | Recommended Approach |
|----------|---------------------|
| Simple tests, few shared dependencies | `TestFixtureBuilder.GatewayFixture` |
| Many tests sharing same setup | Inherit from `GatewayTestBase` |
| Need custom test doubles (TestableGatewayService) | Use builder for config only |
| Tests with isolated/special config needs | Use builder with `configure` action |
| Need temp directory auto-cleanup | Both approaches handle this |

## Test Isolation Guarantees

1. **Temp Directory**: Each test gets unique temp folder via `Guid.NewGuid()`
2. **Device Keypair**: Fresh keypair generated per fixture
3. **EventSource**: Fresh instance per fixture
4. **Mock Reset**: Mocks recreated per test (xUnit default)
5. **Cleanup**: `IDisposable.Dispose()` deletes temp folder

## Rollback Plan

If issues arise:
1. Keep original test files alongside refactored versions
2. Remove refactored files from `.csproj` temporarily
3. Original tests remain functional

## Benefits Summary

| Metric | Before | After |
|--------|--------|-------|
| Lines of setup per test | ~5-8 | ~1-2 |
| Unique temp dirs | Manual | Automatic |
| Config consistency | Variable | Standardized |
| New test creation | Copy/paste | Use fixture |
| Cleanup | Manual | Automatic |

## Next Steps

1. ✅ Create `GatewayTestBase.cs` with builder and base class
2. ✅ Create example refactored tests
3. ⬜ Review with team
4. ⬜ Migrate `GatewayClientTests.cs`
5. ⬜ Migrate `GatewayConnectionLifecycleTests.cs`
6. ⬜ Migrate `GatewayServiceStabilityTests.cs`
7. ⬜ Delete example files (`*_Refactored.cs`)
8. ⬜ Document in project wiki
