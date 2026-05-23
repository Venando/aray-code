using System.Text;

namespace OpenClawPTT.TTS.Providers.Supertonic;

/// <summary>
/// Holds embedded Python scripts and TOML configuration used by the
/// Supertonic 3 TTS uv-managed Python environment.
/// </summary>
internal static class SupertonicScripts
{
    /// <summary>
    /// Content for <c>pyproject.toml</c> — declares supertonic + soundfile
    /// dependencies for the uv-managed Python project.
    /// supertonic has no strict Python upper bound, unlike Coqui (which needs &lt;3.12).
    /// </summary>
    internal static string PyProjectToml => """
[project]
name = "openclaw-ptt-supertonic"
version = "0.1.0"
requires-python = ">=3.9"
dependencies = [
    "supertonic>=1.3.1",
    "soundfile>=0.12.0",
]
""";

    /// <summary>
    /// Content for <c>supertonic_service.py</c> — long-running Supertonic 3 TTS
    /// service that reads JSON requests from stdin and writes JSON responses to stdout.
    ///
    /// Protocol:
    ///   Request:  {"id":"&lt;guid&gt;","text":"...","voice":"M1","lang":"en","quality":8,"speed":1.05}
    ///   Response: {"type":"ok","id":"&lt;guid&gt;","path":"/tmp/...wav","time":0.15}
    ///   Error:    {"type":"error","id":"&lt;guid&gt;","msg":"..."}
    ///   Ready:    {"type":"ready"}
    ///   Stop:     Send "EXIT" on stdin
    /// </summary>
    internal static string ServiceScript => """
#!/usr/bin/env python3
import sys, json, os, tempfile, time, traceback

# ── UTF-8 stdout ──
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

def send(msg):
    print(json.dumps(msg), flush=True)

def protocol(msg_type, **kwargs):
    send({"type": msg_type, **kwargs})

# ── Load model ──
from supertonic import TTS

tts = TTS(auto_download=True)
VOICE_CACHE = {}

def get_voice(name):
    if name not in VOICE_CACHE:
        VOICE_CACHE[name] = tts.get_voice_style(voice_name=name)
    return VOICE_CACHE[name]

protocol("ready")

# ── Main loop ──
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
    if not text:
        protocol("error", id=req_id, msg="Missing required field: text")
        continue

    try:
        voice_name = req.get("voice", "M1")
        lang = req.get("lang", "en")
        quality = req.get("quality", 8)
        speed = req.get("speed", 1.05)

        voice = get_voice(voice_name)
        t0 = time.monotonic()
        wav, duration = tts.synthesize(
            text=text,
            lang=lang,
            voice_style=voice,
            total_steps=quality,
            speed=speed,
        )
        elapsed = time.monotonic() - t0

        import soundfile as sf
        out = tempfile.mktemp(suffix=".wav", prefix=f"supertonic_{req_id}_")
        sf.write(out, wav.squeeze(), 44100)
        file_size = os.path.getsize(out)

        protocol("ok", id=req_id, path=out, time=round(elapsed, 3), bytes=file_size)
    except Exception:
        traceback.print_exc(file=sys.stderr)
        protocol("error", id=req_id, msg="Supertonic TTS synthesis failed")
""";
}
