# SRP/DRY Audit: Services/Status

**Branch:** `audit/srp-dry-status-services-audit` (based on `origin/main` `16e2e73`)
**Date:** 2026-05-13

## Files Covered (19 files)

```
Services/Status/
├── IStatusService.cs           # Interface
├── IStatusPart.cs              # Interface
├── StatusPartBase.cs           # Abstract base (dirty-flag + cache)
├── StringStatusPartBase.cs     # Abstract base (string-valued parts)
├── DisplayPosition.cs          # Enum
├── StatusColor.cs              # Enum
├── StatusColorExtensions.cs    # Extension method
├── StatusAnimationManager.cs   # Timer for yellow dot animation
├── StatusService.cs            # Central orchestrator
├── StatusRenderer.cs           # Rendering/composition pipeline
├── DirectLlmProbeService.cs    # Probe lifecycle (uses IStatusService)
└── StatusParts/
    ├── ActiveAgentPart.cs
    ├── ModelPart.cs
    ├── ThinkingLevelPart.cs
    ├── ContextPart.cs
    ├── ConversationNamePart.cs
    ├── ServiceStatusPart.cs
    ├── DirectLlmStatusPart.cs      # ⚠️ DEAD CODE
    └── MainAgentsPart.cs
```

## SRP Assessment

### ✅ Clean Separations

| Class | Responsibility | Status |
|-------|---------------|--------|
| `StatusService` | Owns status state + lifecycle + event wiring | ✅ Good |
| `StatusRenderer` | Composition + rendering to StreamShellHost | ✅ Good — clean extraction |
| `StatusAnimationManager` | Timer lifecycle for yellow-dot animation | ✅ Good — clear SRP |
| `StatusColorExtensions` | StatusColor → Spectre markup string | ✅ Good |
| `StatusPartBase` | Dirty-flag tracking + StringBuilder caching | ✅ Good |
| `StringStatusPartBase` | String-valued value tracking + dirty-on-change | ✅ Good |
| Each `*Part` | Renders one specific status element | ✅ Good |

### ⚠️ Issues Found

**1. DirectLlmStatusPart — DEAD CODE**
- File: `StatusParts/DirectLlmStatusPart.cs`
- Defined with richer LLM status (last-called timestamp, status labels) but **never instantiated**
- `StatusService` uses a plain `ServiceStatusPart` (`_llmStatusPart`) for LLM status instead
- `StatusService.SetDirectLlmLastCalled()` is a no-op — confirms this feature was abandoned
- ~60 lines of dead code. Delete it.

**2. StatusService.SetGatewayStatus / SetTtsStatus / SetSttStatus / SetDirectLlmStatus — interface/contract drift**
- All 4 interface methods accept a `string label` parameter
- `StatusService.SetServiceStatus()` only passes `StatusColor` — **the label is silently discarded**
- External callers (AppRunner, ReconnectCommand, DirectLlmProbeService) all pass meaningful labels like `"Connected"`, `"Disconnected"`, `"Reconnecting"`
- The UI has moved to dot-only display with 2-character prefixes (GW:/TTS:/STT:/LLM:), so labels are irrelevant
- **Fix:** Remove `label` parameter from all Set*Status interface methods, or collapse to `SetStatus(ServiceKind kind, StatusColor color)` with a `ServiceKind` enum (GW, TTS, STT, LLM)

**3. StatusService.SeparatorBefore — code in app config with no need for positions**
- Not an SRP violation but worth noting: DisplayPosition has 6 values but only TopSeparatorLeft/TopSeparatorRight are actually used for individual parts. Bottom positions and AppPanel positions are effectively MainAgentsPart-only.
- Not actionable unless config-driven per-part positioning is considered overengineered.

**4. StatusService couples to AgentRegistry global singleton**
- `AgentRegistry.ActiveSessionChanged` is a static event — StatusService subscribes directly
- This is a hidden dependency not expressed through the constructor
- Creates issues in testing (static state mutation)
- **Mitigation:** Already partially addressed by `SetAgentStatusTracker()` injection pattern. Recommend wrapping `ActiveSessionChanged` into an injectable `ISessionEventHandler`.

**5. ServiceStatusPart.CurrentYellowChar — DEAD CODE**
- `internal char CurrentYellowChar` getter defined but never referenced outside `ServiceStatusPart`
- Animation rendering happens entirely within `BuildText()` — this property is unused private infrastructure
- Remove it.

**6. ServiceStatusPart.GetYellowFrame / SolidDot — borderline utility**
- `protected static char GetYellowFrame(int index)` — used only in `BuildText()` via `YellowFrames[_frameIndex]`
- `protected static char SolidDot` — unused (buildtext uses `"\u25CF"` directly)
- Both are either dead or add no value. `SolidDot` is dead; `GetYellowFrame` might be useful for subclasses but no subclass uses it (DirectLlmStatusPart overrides BuildText entirely).

## DRY Assessment

### ✅ Good (recent refactoring fixed these)

- `StringStatusPartBase` — eliminates duplicated `Update/Clear/Value/dirty-on-change` across 3+ parts
- `StatusColorExtensions.ToMarkupColor()` — eliminates repeated `switch` in ServiceStatusPart
- `StatusRenderer.MarkAllClean()` — single iteration instead of per-caller ad-hoc cleanup
- `_animatedParts` array — collects ServiceStatusPart instances for efficient ticking
- `Mutate()` helper — eliminates lock+render repetition across all setters

### ⚠️ Remaining Issues

**1. 4× identical Set*Status methods with unused label**
- `SetGatewayStatus(label, color)` / `SetTtsStatus(label, color)` / `SetSttStatus(label, color)` / `SetDirectLlmStatus(label, color)` all delegate to the same `SetServiceStatus(part, color)` — every last one discards `label`
- **Fix:** Either strip `label` from the interface (breaking change to 4 callers) or introduce a single `SetServiceStatus(ServiceKind kind, StatusColor color)` method (preferred: more future-proof)

**2. ActiveAgentPart.Update always marks dirty**
- Unlike every other part (StringStatusPartBase, ContextPart, ServiceStatusPart.SetStatus), `ActiveAgentPart.Update()` calls `MarkDirty()` unconditionally, even when the snapshot reference hasn't changed
- Comment says: _"Re-check on every update anyway — snapshot is a record and may have been replaced"_
- This bypasses the caching that `StatusPartBase` was designed for. On every agent status change tick (frequent), ActiveAgentPart recreates its markup even if nothing changed.
- **Fix:** Track last-rendered agent identity data (session key, display name, status emoji) and only mark dirty on actual change

**3. Spectre markup wrapping pattern repeated**
- Pattern `"[${color}]${content}[/]"` appears in:
  - `ServiceStatusPart.BuildText()` — dot wrapping
  - `ActiveAgentPart.BuildText()` — agent name wrapping
  - `ConversationNamePart.BuildText()` — conversation name wrapping
  - `ContextPart` — doesn't use markup wrapping (just plain text)
- Could extract a small helper: `Builder.AppendMarkup(ReadOnlySpan<char> color, ReadOnlySpan<char> content)`
- Borderline utility — the pattern is simple (3 `.Append()` calls). Only 3 occurrences. **Low priority.**

**4. ServiceStatusPart.SetStatus duplicate overloads**
- `SetStatus(StatusColor)` and `SetStatus(string label, StatusColor)` — the latter is virtual and marked for subclasses
- Only `DirectLlmStatusPart` overrides the 2-arg version to track `_statusLabel`
- But DirectLlmStatusPart is dead code.
- If we remove DirectLlmStatusPart, the 2-arg virtual overload is unused dead code.
- **Fix:** Collapse to single `SetStatus(StatusColor)` method. Remove virtual overload.

**5. StatusService constructor vs SetAgentStatusTracker dual injection**
- `IAgentStatusTracker?` can be passed via constructor OR via `SetAgentStatusTracker()` method
- The constructor also subscribes to `AgentRegistry.ActiveSessionChanged` regardless of whether a tracker was provided
- Two ways to wire the same dependency is confusion, not DRY
- **Fix:** Pick one injection path. Prefer constructor injection.

## Summary of Actionable Items

### High Priority
- [ ] **Delete `DirectLlmStatusPart.cs`** — dead code, ~60 lines
- [ ] **Remove `label` from IStatusService Set*Status methods** or collapse to single `SetServiceStatus(ServiceKind, StatusColor)` — contract drift, every caller passes labels that are discarded
- [ ] **Delete unused `ServiceStatusPart.CurrentYellowChar`** — dead property
- [ ] **Delete unused `ServiceStatusPart.SolidDot`** — dead property

### Medium Priority
- [ ] **Fix `ActiveAgentPart.Update`** to check actual value change before marking dirty (respect caching)
- [ ] **Remove virtual `SetStatus(string, StatusColor)` overload** from ServiceStatusPart (only needed by dead DirectLlmStatusPart)
- [ ] **Resolve dual-injection** of IAgentStatusTracker (constructor vs SetAgentStatusTracker)

### Low Priority
- [ ] **Extract Spectre markup helper** for `[color]content[/]` pattern (3 occurrences)
- [ ] **Wrap AgentRegistry.ActiveSessionChanged** into injectable interface for testability

## Tests

**Current test coverage for Services/Status: ZERO.**
- No test files reference StatusService, StatusRenderer, StatusPart classes, MainAgentsPart, or ServiceStatusPart.
- `DirectLlmProbeService` also has no dedicated tests.
- Callers (AppRunner, ReconnectCommand, LlmCommand) are tested but not the status service itself.
