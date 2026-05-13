# Coqui TTS Pipeline Resilience Analysis

**Branch**: `analysis/tts-pipeline-resilience`
**Date**: 2026-05-13
**Trigger**: CoquiUvEnvironment / CoquiTtsConfigFlow initialization failures, unstable pipeline, unhelpful logging.

---

## Root Cause

**Python 3.13.3 is too new for Coqui TTS.** The `pyproject.toml` declares `requires-python = ">=3.9"` but Coqui TTS (v0.22.0) hard-requires `python >= 3.9 and < 3.12`. Because `uv` sees the system Python 3.13.3 satisfies `>=3.9`, it tries to build `tts==0.22.0` with 3.13.3 — and fails with:
```
RuntimeError: TTS requires python >= 3.9 and < 3.12 but your Python version is 3.13.3
```

**Fix**: `pyproject.toml` needs `requires-python = ">=3.9,<3.12"`. With this constraint, `uv` will automatically download a compatible Python version (e.g., 3.11) — this is one of `uv`'s key features.

---

## Issues Found

### 1. Missing Python Version Cap in pyproject.toml

**File**: `CoquiUvEnvironment.cs` (embedded `PyProjectToml` constant)
**Severity**: 🔴 Critical — prevents any Coqui TTS functionality

```toml
requires-python = ">=3.9"  # ❌ Should be ">=3.9,<3.12"
```

Without the `<3.12` cap, `uv` uses system Python 3.13+ which can't build `tts==0.22.0`.

### 2. No Python Version Pre-check

**File**: `CoquiUvEnvironment.cs`
**Severity**: 🟡 High

Neither `CoquiUvEnvironment` nor `CoquiTtsConfigFlow` checks the actual Python version before triggering `uv run`. The code jumps straight into package resolution, downloads 109+ MiB of torch, 68+ MiB of dependencies, only to fail at the very end with the version error.

**Fix**: Add a static check `ValidatePythonVersion()` that runs `uv python find` and verifies the resolved version is in `[3.9, 3.12)` before any expensive operation.

### 3. Auto-download Without User Confirmation

**File**: `CoquiTtsConfigFlow.RunAsync()`
**Severity**: 🔴 Critical — UX issue

```csharp
// Pre-download if not cached
if (!CoquiTtsModelManager.IsModelCached(modelResult))
{
    await DownloadModelAsync(host, modelManager, modelResult, ct);
}
```

After model selection, the code immediately starts downloading without asking. This:
- Starts downloading potentially gigabytes without user consent
- Blocks the config wizard until download completes (or fails)
- Partially modifies config (sets `CoquiModelName`) before download succeeds
- If download fails, config is in inconsistent state

**Fix**: After model selection, ask "Download now or just select?" before initiating download. Config should be saved only after successful download, or at least offer a "select without downloading" option.

### 4. Redundant Error Logging (Same Error × 3+)

**Severity**: 🟡 Medium — floods user with noise

The same `Failed to build tts==0.22.0` traceback appears 3+ times:
1. **Model fetch** (`FetchFromUvAsync`) — fails to resolve dependencies, full traceback printed
2. **Download attempt 1** (`DownloadModelAsync`) — same build failure, full traceback again
3. **Provider init retries** (`EnsureRunningAsync`) — retries 3×, each logging the same error

After the first build failure, nothing changes — the Python version is still 3.13.3. Retrying just re-downloads packages and fails identically.

**Fix**: Add static flag `s_uvBuildBroken` — after first build error, short-circuit with "uv environment broken — fix Python version first" instead of retrying the same doomed operation.

### 5. Logging Floods User With Raw Build Output

**Severity**: 🟡 Medium

`DownloadModelAsync` logs every stdout/stderr line to the user:
```csharp
_host.AddMessage($"[grey]      [stdout] {line}[/]");
_host.AddMessage($"[grey]      {EscapeSpectreMarkup(line)}[/]");
```

This produces walls of grey build output for every download, download retry, and provider init attempt. The user sees the same "Downloading torch (109.2MiB)" line 3+ times.

**Fix**: Route build output to a collapsible bottom panel or progress indicator instead of AddMessage. Only show errors to the user.

### 6. Pipeline Phase Coupling — Every Phase Runs uv

**Severity**: 🟡 Medium — causes cascading failures

The pipeline has 4 phases, each independently running `uv run`:
1. `FetchFromUvAsync` — `TTS().list_models()` (model discovery)
2. `DownloadModelAsync` — `TTS(model_name="...")` (pre-download)
3. `EnsureRunningAsync` — `tts_service.py` (runtime provider)
4. Retry loop — 3× retries with exponential backoff

When phase 1 fails to resolve dependencies, phases 2-4 will also fail for the same reason. But there's no short-circuit — each phase tries independently, downloads packages fresh, and produces the same error.

**Fix**: Consolidate environment validation into a single gate. If the uv environment can't resolve dependencies, stop the entire pipeline with a clear message.

### 7. Inconsistent Spectre Markup Escaping

**Severity**: 🟢 Low — cosmetic

`EscapeSpectreMarkup` is defined in `CoquiTtsModelManager` but used inconsistently:
- `CoquiTtsConfigFlow.SelectModelAsync` calls `EscapeLine` (inline helper)
- `CoquiTtsModelManager.GetAvailableModelsAsync` calls `EscapeSpectreMarkup`
- `CoquiTtsModelManager.DownloadModelAsync` sometimes escapes, sometimes doesn't

**Fix**: Use a shared helper consistently. The codebase already has `Markup.Escape()` from Spectre.Console — prefer that.

### 8. Config Modified Before Download Completes

**Severity**: 🟡 Medium — inconsistent state

```csharp
config.CoquiModelName = modelResult;  // changed BEFORE download
changed = true;
// ...
if (!CoquiTtsModelManager.IsModelCached(modelResult))
{
    await DownloadModelAsync(host, modelManager, modelResult, ct);  // may throw
}
```

If download throws, `CoquiModelName` is already set but the model isn't cached. Next startup will try to use it and fail at TTS init.

**Fix**: Move `config.CoquiModelName = modelResult` after successful download (or after user confirms "select without download").

---

## Proposed Fixes

### Fix 1: Pin Python in pyproject.toml

```csharp
requires-python = ">=3.9,<3.12"
```

With this, `uv` will download a compatible Python (e.g., 3.11) automatically.

### Fix 2: Add Environment Validation Gate

New method in `CoquiUvEnvironment`:

```csharp
public static async Task<EnvironmentValidationResult> ValidateAsync(
    string? dataDir, CancellationToken ct)
```

Runs a lightweight check: `uv python find` → validate version range. Does NOT trigger full package resolution. Called from `CoquiTtsConfigFlow` before the model selection flow.

If the resolved Python version is incompatible:
- Show clear error: "Python 3.13.3 is too new. Coqui TTS requires < 3.12."
- If uv can manage Python versions (it can): "uv will download Python 3.11 automatically."
- If uv can't: "Install Python 3.11 and add it to PATH."

### Fix 3: Gate Download Behind User Confirmation

After model selection in `CoquiTtsConfigFlow`:
```
Selected: tts_models/en/jenny/jenny
  [1] Download now (~50 MB)
  [2] Select without downloading
  [3] Cancel
```

Only set `config.CoquiModelName` after the user's choice.

### Fix 4: Add Static Broken Flag

```csharp
private static bool s_uvBuildBroken;
private static string? s_uvBuildErrorDetail;
```

Set after first build failure. Subsequent calls to `FetchFromUvAsync`, `DownloadModelAsync`, or `EnsureRunningAsync` short-circuit with:
"Cannot proceed — uv environment is broken: {error}. Fix Python version and restart."

### Fix 5: Reduce Log Noise

- Move stderr streaming to bottom panel or progress indicator
- Only show error summaries to user, not raw stderr tracebacks
- Collapse repeated errors into "Build failed (Python 3.13.3 is too new for Coqui TTS)" — one line, not 30

### Fix 6: Consolidate Environment Check

Single `CoquiUvEnvironment.EnsureReadyAsync()` that:
1. Checks uv is installed
2. Validates Python version
3. Resolves dependencies (triggers build if needed)
4. Sets static flags

Called once from the top-level config flow. All downstream consumers check flags instead of re-running uv.

---

## Implementation Order

1. Fix `pyproject.toml` (`requires-python` cap) — immediate relief
2. Add `ValidateAsync` gate in `CoquiUvEnvironment`
3. Gate download behind user confirmation in `CoquiTtsConfigFlow`
4. Add static broken flag
5. Clean up logging
6. Consolidate environment checks
