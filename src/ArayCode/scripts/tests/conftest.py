"""
Pytest configuration and shared fixtures for TTS script tests.
"""

import sys
import os

# Add scripts folder to path for imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import pytest
import tempfile
import time
from io import StringIO


class MockEnv:
    """In-memory environment for testing."""
    def __init__(self, values=None):
        self._values = values or {}

    def get(self, key, default=None):
        return self._values.get(key, default)

    def set(self, key, value):
        self._values[key] = value


class MockFileSystem:
    """In-memory file system for testing."""
    def __init__(self):
        self._dirs = set()
        self._files = {}
        self._temp_dir = "/tmp"
        self._next_temp_file = 0

    def makedirs(self, path, exist_ok=False):
        self._dirs.add(path)
        if not exist_ok and path in self._dirs:
            raise FileExistsError(path)

    def getsize(self, path):
        if path not in self._files:
            raise FileNotFoundError(path)
        return len(self._files[path])

    def write_file(self, path, content):
        self._files[path] = content
        self._dirs.add(os.path.dirname(path))

    def read_file(self, path):
        if path not in self._files:
            raise FileNotFoundError(path)
        return self._files[path]

    def temp_dir(self):
        return self._temp_dir

    def temp_file(self, suffix="", delete=True):
        self._next_temp_file += 1
        path = f"{self._temp_dir}/temp_{self._next_temp_file}{suffix}"
        self._files[path] = b""
        class TempFile:
            def __init__(self, name, delete):
                self.name = name
                self._delete = delete
            def __enter__(self):
                return self
            def __exit__(self, *args):
                if self._delete and self.name in self._files:
                    del self._files[self.name]
        return TempFile(path, delete)


class MockClock:
    """Fake clock for deterministic testing."""
    def __init__(self):
        self._time = 0.0

    def monotonic(self):
        return self._time

    def advance(self, seconds):
        self._time += seconds


class MockStreamIO:
    """In-memory stream I/O for testing."""
    def __init__(self, stdin_content=""):
        self._stdin = StringIO(stdin_content)
        self._stdout = StringIO()
        self._stderr = StringIO()
        self._lines_read = []

    def read_line(self):
        line = self._stdin.readline()
        if not line:
            return None
        line = line.strip()
        self._lines_read.append(line)
        return line

    def write_json(self, obj):
        import json
        self._stdout.write(json.dumps(obj) + "\n")

    def write_raw(self, text):
        self._stdout.write(text + "\n")

    def get_stdout(self):
        return self._stdout.getvalue()

    def get_stderr(self):
        return self._stderr.getvalue()

    def inject_lines(self, lines):
        """Add lines to stdin for reading."""
        self._stdin = StringIO("\n".join(lines) + "\n")


class MockLogger:
    """Capturing logger for testing."""
    def __init__(self):
        self._records = []

    @property
    def records(self):
        return self._records

    def addHandler(self, handler):
        pass

    def info(self, msg, *args, **kwargs):
        self._records.append(("INFO", msg % args if args else msg))

    def warning(self, msg, *args, **kwargs):
        self._records.append(("WARNING", msg % args if args else msg))

    def error(self, msg, *args, **kwargs):
        self._records.append(("ERROR", msg % args if args else msg))

    def exception(self, msg, *args, **kwargs):
        self._records.append(("EXCEPTION", msg % args if args else msg))

    def debug(self, msg, *args, **kwargs):
        self._records.append(("DEBUG", msg % args if args else msg))


@pytest.fixture
def mock_env():
    return MockEnv()


@pytest.fixture
def mock_fs():
    return MockFileSystem()


@pytest.fixture
def mock_clock():
    return MockClock()


@pytest.fixture
def mock_stream():
    return MockStreamIO()


@pytest.fixture
def mock_log():
    return MockLogger()
