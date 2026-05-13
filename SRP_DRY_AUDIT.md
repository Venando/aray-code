# SRP & DRY Audit — openclaw-ptt-client

Audit of `e3824b2` ("fixed conversation naming service subscriptions"). All findings are ordered by ease of fix (easiest first).

---

## DRY Violations (Easiest Fixes)

### 1. `EditToolRenderer` implements `IToolRenderer` directly (not `ToolRendererBase`)

**File:** `Renderers/EditToolRenderer.cs:12`

Every other tool renderer extends `ToolRendererBase` — `EditToolRenderer` is the only one that manually wires `IToolOutput` and has no access to `PrintValue()`, `PrintLabelValue()`, `PrintPropertyIfExists()` etc.

**Fix:** Change to `ToolRendererBase`, replace `_output.Print` → `Output.Print`, `_output.PrintLine` → `Output.PrintLine`.

**Risk:** Near-zero (remove ~5 lines of boilerplate).

---

### 2. `DefaultToolRenderer` and `GenericKvpToolRenderer` are nearly identical

Both iterate `JsonElement` properties and print key-value pairs in the same pattern:
- First prop: `Print(value, Gray)` 
- Subsequent props: `Print(", key: ", DarkGray)` + `Print(value, White)`

**Fix:** Extract shared "iterate JSON properties → render KVP" logic into `ToolRendererBase` or a static helper.

**Risk:** Low. Test by comparing rendered output for each.

---

### 3. `ConnectionStatusPart` / `DirectLlmStatusPart` share a `ToMarkupColor()` method

Both files have an identical `private static string ToMarkupColor(StatusColor color)` method.

**Fix:** Move `ToMarkupColor(StatusColor)` to `StatusPartBase` as `protected` so all parts share it.

**Risk:** None (pure method, no side effects).

---

### 4. `ExecToolRenderer.RenderCommand()` repeats `Output.Print(" ", ConsoleColor.White)` ~8× for spacing between tokens

Lines 97, 98, 105, 106, 124, 129, 130, 137, 138 — each time a space is printed between rendered tokens.

**Fix:** Add a private helper `void PrintSpace() => Output.Print(" ", ConsoleColor.White);` and replace all `Output.Print(" ", ConsoleColor.White)` calls with `PrintSpace()`.

**Risk:** Near-zero.

---

### 5. `ActiveAgentPart.TryGetPersistedEmoji()` / `TryGetPersistedColor()` both read the same JSON file

Both call `AgentSettingsPersistenceLegacy.GetPersistedXxx(agentId)` — same JSON deserialization and `try/catch` pattern.

**Fix:** Add a single method like `AgentInfo? TryGetRegistryAgent(string agentId)` to `ActiveAgentPart` to cache the deserialized registry result. Or add a helper to `AgentSettingsPersistenceLegacy` that returns both at once.

**Risk:** Low (minor refactor).

---

## SRP Violations (Easy–Medium Fixes)

### 6. `EditToolRenderer` does two things: file-path display + diff rendering

**File:** `Renderers/EditToolRenderer.cs — 66 lines`

It constructs a `DiffRenderer`, handles file path display, iterates edits array, AND decides between plain text vs diff rendering.

**Fix:** Delegate the "file path" display logic to a shared `FilePathRenderer` helper, keep `EditToolRenderer` as a thin coordinator. (Or just have the path display handled elsewhere.)

**Risk:** Low (the file is small, easy to reorganize).

---

### 7. `SpectreTableRenderer` co-locates table model + parser + aligner + renderer in 554 lines

**File:** `ConsoleTextFormatter/SpectreTableRenderer.cs`

`MarkdownTable` is an inner class (column count, alignments, formatted rows). Parsing, alignment calculation, and Spectre rendering are all in `SpectreTableRenderer` static methods.

**Fix:** Extract `MarkdownTable` to its own file. Extract table parsing into a `MarkdownTableParser` static class. Keep `SpectreTableRenderer` for rendering only.

**Risk:** Medium (tests may exist that import from `SpectreTableRenderer` directly). Check test imports first — if they only test via `AgentReplyFormatter`, this is safe.

---

### 8. `AgentStatusExtractor` does parsing + merging + history parsing in 455 lines

**File:** `Services/AgentStatus/AgentStatusExtractor.cs`

Single static class with:
- `Extract(IColorConsole, JsonElement, existing)` — parses gateway event payloads
- `MergeSnapshots(existing, incoming)` — merge logic
- `FromHistoryMessage(msg, sessionKey)` — history message parsing  
- 7 private `GetXxx` helpers (GetString, GetLong, GetInt, GetBool, GetDecimal, TryGetObject, GetStringArray)

**Fix:** Extract `MergeSnapshots` → dedicated `AgentStatusMerger` class. Keep `Extract` and `FromHistoryMessage` in extractor.

**Risk:** Low (static methods, no state). Update call sites in `AgentStatusTracker`.

---

### 9. `PythonTtsProvider` handles process lifecycle + synthesis + restart logic (470 lines)

**File:** `TextToSpeach/Providers/PythonProvider/PythonTtsProvider.cs`

Single class manages: process start/stop, startup detection (READY line), restart backoff (exponential), pending request tracking (ConcurrentDictionary), synthesis timeouts, and restart loop prevention.

**Fix:** Extract `PythonProcessManager` (start, stop, restart, health) and `PendingRequestTracker` (ConcurrentDictionary TCS management). Keep synthesis orchestration in `PythonTtsProvider`.

**Risk:** Medium (process management is nuanced — many edge cases). Verify restart logic still works after extraction.

---

### 10. `WhisperCppModelManager` does model listing + download + delete + static registry (614 lines)

**File:** `PushToTalk/SpeachToText/WhisperCppModelManager.cs`

Static model list (available models), download management, file system operations, and progress reporting.

**Fix:** Extract `WhisperModelRegistry` (static model list + info). Extract `WhisperModelDownloader` (download + progress). Keep orchestration in `WhisperCppModelManager`.

**Risk:** Medium (file system + concurrent downloads). Test with actual download scenarios.

---

### 11. `StatusService` manages part lifecycle + dirty tracking + rendering (406 lines)

**File:** `Services/Status/StatusService.cs`

Creates parts, manages their lifecycle, feeds data into them, collects positions, composes text, and pushes to shell host.

**Fix:** The status parts architecture already improves SRP. But `StatusService` still has too many `SetXxx` forwarding methods. Consider extracting a `StatusPartComposer` that handles position grouping and composition.

**Risk:** Low–medium (compositor is stable, but `StatusService` is used everywhere).

---

### 12. `AppRunner.RunPttLoopAsync()` does too much (130+ lines in one method)

**File:** `AppRunner.cs:208–291`

Single method: creates audio service, wires config events, creates text sender, naming service, input handler, PTT controller, hotkey service, shell commands, snapshot cleaner, registers commands, prints help menu, creates PTT loop. And manages disposal via `try/finally`.

**Fix:** Extract creation groups into private factory methods:
- `CreateNamingPipeline(...)`  
- `CreateShellCommands(...)`
- `CreateHotkeyServices(...)`

**Risk:** Low (pure extraction, no logic change).

---

## Summary

| # | Issue | Type | Effort | Tools | Priority |
|---|-------|------|--------|-------|----------|
| 1 | `EditToolRenderer` not using `ToolRendererBase` | DRY | minutes | ✅ verifiable | High |
| 2 | `DefaultToolRenderer` / `GenericKvpToolRenderer` duplicate KVP pattern | DRY | minutes | ✅ verifiable | Medium |
| 3 | Duplicate `ToMarkupColor()` in 2 status parts | DRY | minutes | ✅ verifiable | Medium |
| 4 | Repeated `Output.Print(" ", ...)` in `ExecToolRenderer` | DRY | minutes | ✅ verifiable | Low |
| 5 | Duplicate JSON read in `ActiveAgentPart` | DRY | minutes | ✅ verifiable | Low |
| 6 | `EditToolRenderer` path display + diff | SRP | 10 min | ✅ compile + tests | Medium |
| 7 | `SpectreTableRenderer` model + parser + renderer | SRP | 15 min | ✅ compile + tests | Medium |
| 8 | `AgentStatusExtractor` parsing + merge + helpers | SRP | 15 min | ✅ compile + tests | Medium |
| 9 | `PythonTtsProvider` process + synthesis | SRP | 30 min | ⚠️ manual testing needed | Low |
| 10 | `WhisperCppModelManager` listing + download + delete | SRP | 30 min | ⚠️ manual testing needed | Low |
| 11 | `StatusService` orchestrator with forwarding methods | SRP | 20 min | ✅ compile + tests | Low |
| 12 | `AppRunner.RunPttLoopAsync()` too long | SRP | 10 min | ✅ compile + tests | Medium |

Items #1–8 are the sweet spot: high signal, low risk, easy to verify with existing tests.

---

*Generated from `analysis/srp-dry-audit` branch based on `e3824b2` (origin/main @ 2026-05-13)*
