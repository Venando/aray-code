"""
TTS Service — startup, request handling, and main loop.
All abstractions are imported from siblings; engine from sibling module.
"""

import sys
from datetime import datetime

from abstractions import (
    log, env, fs, clock, io, send, sendProtocol, _stdout_to_stderr,
)
from engine import CoquiTTSEngine


# ── Global state ───────────────────────────────────────────────────────────────
tts: object | None = None
_started: bool = False


def start() -> None:
    """Initialize logging and load the TTS model. Safe to call multiple times."""
    global tts, _started
    if _started:
        return
    _started = True

    # Persistent log file
    _log_dir = env.get("TTS_LOG_DIR", __import__('os').path.join(fs.temp_dir(), "aray-code-logs"))
    fs.makedirs(_log_dir, exist_ok=True)
    _log_file = __import__('os').path.join(_log_dir, f"tts_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log")
    _file_handler = __import__('logging').FileHandler(_log_file, encoding="utf-8")
    _file_handler.setLevel(__import__('logging').INFO)
    _file_handler.setFormatter(__import__('logging').Formatter("[%(asctime)s] %(levelname)s: %(message)s"))
    log.addHandler(_file_handler)

    model_name = env.get("TTS_MODEL") or None
    model_path = env.get("TTS_MODEL_PATH") or None
    tts_config_path = env.get("TTS_CONFIG_PATH") or None

    log.info("=== TTS Service starting ===")
    log.info(f"Log file: {_log_file}")
    log.info(f"TTS_MODEL env: {model_name!r}")
    log.info(f"TTS_MODEL_PATH env: {model_path!r}")
    log.info(f"TTS_CONFIG_PATH env: {tts_config_path!r}")
    log.info(f"Python version: {sys.version}")
    log.info(f"Python executable: {sys.executable}")

    if model_name or model_path:
        engine = CoquiTTSEngine(model_name, model_path, tts_config_path, clock, log)
        try:
            engine.ensure_ready()
            tts = engine
            sendProtocol({"type": "ready"})
        except Exception as e:
            log.exception(f"Critical failure: could not load TTS model on GPU or CPU: {e}")
            sendProtocol({"type": "error", "id": "startup", "msg": str(e)})
            sys.stdout.flush()
            sys.exit(1)


# ── Request handler ────────────────────────────────────────────────────────────
def handle_request(data: dict) -> None:
    req_id = data.get("id", "unknown")
    text = data.get("text")
    voice = data.get("voice")
    model = data.get("model")

    if not text:
        sendProtocol({"type": "error", "id": req_id, "msg": "Missing required field: text"})
        return

    if tts is None:
        sendProtocol({"type": "error", "id": req_id, "msg": "No TTS model loaded"})
        return

    try:
        with fs.temp_file(suffix=".wav", delete=False) as f:
            out_path = f.name

        t0 = clock.monotonic()
        with _stdout_to_stderr():
            tts.tts_to_file(text=text, file_path=out_path, voice=voice, model=model)

        file_size = fs.getsize(out_path)
        elapsed = clock.monotonic() - t0
        log.info(__import__('json').dumps({"perf": True, "id": req_id, "bytes": file_size, "time": round(elapsed, 2)}))
        sendProtocol({"type": "ok", "id": req_id, "path": out_path})

    except Exception:
        log.exception(f"Request {req_id} failed")
        sendProtocol({"type": "error", "id": req_id, "msg": "TTS synthesis failed"})


# ── Main stdin loop ────────────────────────────────────────────────────────────
def run() -> None:
    start()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break
        try:
            handle_request(__import__('json').loads(line))
        except __import__('json').JSONDecodeError:
            try:
                req_id = __import__('json').loads(line[:256]).get("id", "unknown")
            except Exception:
                req_id = "unknown"
            sendProtocol({"type": "error", "id": req_id, "msg": f"Invalid JSON: {line[:100]}"})
