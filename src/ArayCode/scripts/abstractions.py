"""
Abstractions: Clock, Env, FileSystem, StreamIO.
All testable without touching TTS or protocol logic.
"""

import sys
import json
import os
import tempfile
import time
import contextlib
import logging
from abc import ABC, abstractmethod


# ── Logger ──────────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(levelname)s: %(message)s",
    stream=sys.stderr,
)
log = logging.getLogger("tts")


# ── Environment abstraction ─────────────────────────────────────────────────────
class Env(ABC):
    @abstractmethod
    def get(self, key: str, default: str | None = None) -> str | None:
        ...


class SystemEnv(Env):
    def get(self, key: str, default: str | None = None) -> str | None:
        return os.environ.get(key, default)


env: Env = SystemEnv()


# ── FileSystem abstraction ──────────────────────────────────────────────────────
class FileSystem(ABC):
    @abstractmethod
    def makedirs(self, path: str, exist_ok: bool) -> None:
        ...

    @abstractmethod
    def getsize(self, path: str) -> int:
        ...

    @abstractmethod
    def temp_dir(self) -> str:
        ...

    @abstractmethod
    def temp_file(self, suffix: str, delete: bool) -> tuple:
        ...


class SystemFileSystem(FileSystem):
    def makedirs(self, path: str, exist_ok: bool) -> None:
        os.makedirs(path, exist_ok=exist_ok)

    def getsize(self, path: str) -> int:
        return os.path.getsize(path)

    def temp_dir(self) -> str:
        return tempfile.gettempdir()

    def temp_file(self, suffix: str, delete: bool):
        return tempfile.NamedTemporaryFile(suffix=suffix, delete=delete)


fs: FileSystem = SystemFileSystem()


# ── Clock abstraction ───────────────────────────────────────────────────────────
class Clock(ABC):
    @abstractmethod
    def monotonic(self) -> float:
        ...


class SystemClock(Clock):
    def monotonic(self) -> float:
        return time.monotonic()


clock: Clock = SystemClock()


# ── Stream I/O abstraction ──────────────────────────────────────────────────────
class StreamIO:
    def __init__(self, stdin, stdout, stderr):
        self._stdin = stdin
        self._stdout = stdout
        self._stderr = stderr

    def read_line(self) -> str | None:
        line = self._stdin.readline()
        if not line:
            return None
        return line.strip()

    def write_json(self, obj: dict) -> None:
        print(json.dumps(obj), file=self._stdout, flush=True)

    def write_raw(self, text: str) -> None:
        print(text, file=self._stdout, flush=True)


io: StreamIO = StreamIO(sys.stdin, sys.stdout, sys.stderr)


def send(obj: dict) -> None:
    """Routes through io so tests can override the output stream."""
    io.write_json(obj)


def sendProtocol(msg: dict) -> None:
    """Send a structured JSON protocol message to stdout.
    All protocol messages (ready, done, ok) use this to ensure
    C# can dispatch by the 'type' field without string heuristics.
    """
    io.write_json(msg)


@contextlib.contextmanager
def _stdout_to_stderr():
    """Redirect stdout to stderr during TTS synthesis.

    Coqui calls print(sens) internally after splitting sentences, which would
    corrupt the JSON protocol on stdout and crash on non-ASCII text under cp1252.
    Swap the reference directly — never wrap stderr.buffer in a new TextIOWrapper,
    as closing the wrapper would destroy the underlying buffer and kill stderr.
    """
    old = io._stdout
    io._stdout = io._stderr
    try:
        yield
    finally:
        io._stdout = old
