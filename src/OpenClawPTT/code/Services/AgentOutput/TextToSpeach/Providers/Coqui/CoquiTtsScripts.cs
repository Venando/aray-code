using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Holds embedded Python scripts and TOML configuration used by the
/// Coqui TTS uv-managed Python environment. Extracted from
/// <see cref="CoquiUvEnvironment"/> to keep environment management
/// separate from inline script definitions (SRP).
/// </summary>
internal static class CoquiTtsScripts
{
    /// <summary>
    /// Content for <c>pyproject.toml</c> — declares TTS + torch dependencies
    /// for the uv-managed Coqui TTS Python project.
    /// </summary>
    internal static string PyProjectToml => """
[project]
name = "openclaw-ptt-tts"
version = "0.1.0"
requires-python = ">=3.9,<3.12"
dependencies = [
    "TTS>=0.22.0",
    "torch>=2.0.0",
]

[tool.uv]
# uv downloads the right Python automatically if not installed

[tool.uv.extra-build-dependencies]
# pandas 1.5.3 and tts 0.22.0 (dep of TTS) use pkg_resources at build time
# but don't declare setuptools as a build dependency. setuptools>=67.3
# removed pkg_resources, so we pin to an older version that includes it.
pandas = ["setuptools<67"]
tts = ["setuptools<67"]
""";

    /// <summary>
    /// Content for <c>tts_service.py</c> — long-running Coqui TTS service
    /// that reads JSON requests from stdin and writes JSON responses to stdout.
    /// </summary>
    internal static string TtsServiceScript => """
#!/usr/bin/env python3
# Coqui TTS long-running service via uv.
# Reads JSON lines from stdin: {"text":"...","id":"...","voice":null|"path.wav"}
# Outputs JSON to stdout: {"type":"ready|ok|error","id":"...","path":"..."}
# Sends "EXIT" to stop.

import sys, os, json, tempfile, time, logging

# ── UTF-8 stdout ──
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# ── Logging to stderr ──
logging.basicConfig(level=logging.INFO, format="%(message)s", stream=sys.stderr)
log = logging.getLogger("coqui_uv_tts")

# ── Protocol helpers ──
def send(msg):
    print(json.dumps(msg), flush=True)

def protocol(msg_type, **kwargs):
    send({"type": msg_type, **kwargs})

# ── Load model ──
def load_model():
    model_name = os.environ.get("TTS_MODEL")
    model_path = os.environ.get("TTS_MODEL_PATH")
    config_path = os.environ.get("TTS_CONFIG_PATH")

    if not model_name and not model_path:
        raise RuntimeError("No TTS_MODEL or TTS_MODEL_PATH set")

    import torch, functools
    # PyTorch >=2.6 defaults weights_only=True; Coqui TTS models need False
    torch.load = functools.partial(torch.load, weights_only=False)
    from TTS.api import TTS

    can_use_cuda = torch.cuda.is_available()
    log.info("CUDA available: %s", can_use_cuda)

    # Try CUDA first, fall back to CPU if loading fails (OOM, driver mismatch, etc.)
    def _load(use_cuda):
        dev = "cuda" if use_cuda else "cpu"
        t0 = time.monotonic()
        if model_path and config_path:
            if not os.path.isfile(model_path):
                raise ValueError(f"TTS_MODEL_PATH is not a file: {model_path!r}")
            if not os.path.isfile(config_path):
                raise ValueError(f"TTS_CONFIG_PATH is not a file: {config_path!r}")
            t = TTS(model_path=model_path, config_path=config_path, progress_bar=False, gpu=use_cuda)
            if use_cuda:
                t = t.to(dev)
        else:
            t = TTS(model_name=model_name, progress_bar=False, gpu=use_cuda)
            if use_cuda:
                t = t.to(dev)
        elapsed = time.monotonic() - t0
        log.info("Coqui TTS loaded on %s in %.1fs", dev.upper(), elapsed)
        return t

    if can_use_cuda:
        try:
            tts = _load(use_cuda=True)
        except Exception:
            log.warning("CUDA load failed, falling back to CPU")
            try:
                torch.cuda.empty_cache()
            except Exception:
                pass
            tts = _load(use_cuda=False)
    else:
        tts = _load(use_cuda=False)

    # Fix for TTS 0.22.0 compat — coqpit configs use __getitem__ / __dict__
    # access. Some downloaded model configs are missing fields that TTS
    # accesses during synthesis (KeyError: 'use_phonemes').
    if tts.config is not None:
        # 'use_phonemes' — missing in some vocoder model configs
        tts.config.__dict__.setdefault("use_phonemes", False)
        # 'languages' — needed for multi-lingual model compat
        tts.config.__dict__.setdefault("languages", [])
    if tts.config is not None and not hasattr(tts, "is_multi_lingual"):
        object.__setattr__(tts, "is_multi_lingual", False)

    return tts

# ── Main ──
def main():
    try:
        tts = load_model()
        protocol("ready")
    except Exception as e:
        log.exception("Failed to load Coqui TTS model")
        protocol("error", id="startup", msg=str(e))
        sys.exit(1)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break

        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            protocol("error", id="unknown", msg=f"Invalid JSON: {line[:100]}")
            continue

        req_id = req.get("id", "unknown")
        text = req.get("text")
        voice = req.get("voice")  # speaker_wav path
        # model switch not supported at runtime — TTS would need reload

        if not text:
            protocol("error", id=req_id, msg="Missing required field: text")
            continue

        try:
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
                out_path = f.name

            t0 = time.monotonic()
            if voice:
                tts.tts_to_file(text=text, file_path=out_path, speaker_wav=voice)
            else:
                tts.tts_to_file(text=text, file_path=out_path)

            elapsed = time.monotonic() - t0
            file_size = os.path.getsize(out_path)
            log.info(json.dumps({"perf": True, "id": req_id, "bytes": file_size, "time": round(elapsed, 2)}))

            protocol("ok", id=req_id, path=out_path)
        except Exception:
            log.exception("Request %s failed", req_id)
            protocol("error", id=req_id, msg="TTS synthesis failed")

if __name__ == "__main__":
    main()
""";

    /// <summary>
    /// Returns the content for a Python helper script that lists cached model paths.
    /// Saved to <c>list_cached.py</c> in the project dir and executed via
    /// <c>uv run python list_cached.py</c>.
    /// Walks the Coqui TTS storage directory (not HuggingFace cache).
    /// </summary>
    internal static string ListCachedModelPathsScript() => """
import os, json, sys

# Coqui TTS uses its own storage, NOT HuggingFace cache.
# Windows: %LOCALAPPDATA%/tts  |  Linux/macOS: ~/.local/share/tts
tts_home = os.environ.get('LOCALAPPDATA')
if tts_home:
    tts_home = os.path.join(tts_home, 'tts')
else:
    tts_home = os.path.expanduser('~/.local/share/tts')

# Priority 2: also check old Coqui TTS dir location
old_home = os.path.join(os.path.expanduser('~'), '.local', 'share', 'tts')

seen = set()
for root_dir in (tts_home, old_home):
    if not os.path.isdir(root_dir):
        continue
    for entry in os.listdir(root_dir):
        full = os.path.join(root_dir, entry)
        if not os.path.isdir(full):
            continue
        # Coqui stores models as tts_models--<lang>--<dataset>--<model>
        # Convert back to standard /-separated format
        for prefix in ('tts_models--', 'vocoder_models--'):
            if entry.startswith(prefix):
                model_name = entry.replace('--', '/')
                seen.add(model_name)
                break

print(f'tts_home={tts_home} found={len(seen)}', file=sys.stderr)
print(json.dumps(sorted(seen)))
""" + "\n";

    /// <summary>
    /// Returns a Python script that fetches model download sizes from the Coqui TTS
    /// model registry. Reads model names from a JSON file (path passed as first argument),
    /// looks up download URLs via the TTS model manager, makes parallel HEAD requests
    /// for Content-Length, caches to a second file, and prints size dict as JSON.
    /// </summary>
    internal static string HfSizesScript => """
import json, sys, os
from concurrent.futures import ThreadPoolExecutor, as_completed
from urllib.request import Request, urlopen
from urllib.error import URLError

def _get_url_size(url):
    '''HEAD request to get Content-Length from a download URL.'''
    try:
        req = Request(url, method='HEAD')
        with urlopen(req, timeout=15) as resp:
            length = resp.headers.get('Content-Length')
            return int(length) if length else None
    except Exception:
        return None

def main():
    names_file = sys.argv[1]
    cache_file = sys.argv[2] if len(sys.argv) > 2 else None

    with open(names_file) as f:
        model_names = json.load(f)

    cache = {}
    if cache_file and os.path.exists(cache_file):
        with open(cache_file) as f:
            cache = json.load(f)

    # Resolve download URLs from the TTS model registry.
    # Use ModelManager directly to avoid TTS() which triggers slow torch import.
    from TTS.utils.manage import ModelManager
    manager = ModelManager()

    # manager.models_dict is a NESTED dict:
    #   models_dict["tts_models"]["en"]["ljspeech"]["vits"] = {"github_rls_url": "...", ...}
    registry = manager.models_dict
    if registry and isinstance(registry, dict):

    URL_FIELDS = ('github_rls_url', 'hf_url', 'url', 'download_url', 'repo_url')
    url_map = {}
    for name in model_names:
        if name in cache:
            continue
        # Navigate nested dict: tts_models/en/ljspeech/vits -> models_dict["tts_models"]["en"]["ljspeech"]["vits"]
        parts = name.split('/')
        meta = registry
        for part in parts:
            if isinstance(meta, dict) and part in meta:
                meta = meta[part]
            else:
                meta = {}
                break
        url = None
        if isinstance(meta, dict):
            for field in URL_FIELDS:
                url = meta.get(field)
                if url:
                    break
        if url:
            url_map[name] = url
        else:

    if url_map:
        sample_items = list(url_map.items())[:3]

    if url_map:
        with ThreadPoolExecutor(max_workers=15) as executor:
            futures = {executor.submit(_get_url_size, url_map[n]): n for n in url_map}
            for future in as_completed(futures):
                name = futures[future]
                size = future.result()
                if size is not None:
                    cache[name] = size

    if cache_file:
        os.makedirs(os.path.dirname(cache_file) or '.', exist_ok=True)
        with open(cache_file, 'w') as f:
            json.dump(cache, f)

    print(json.dumps(cache))

if __name__ == '__main__':
    main()
""" + "\n";
}
