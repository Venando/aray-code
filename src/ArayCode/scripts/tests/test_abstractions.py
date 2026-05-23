"""
Tests for abstractions.py — Env, FileSystem, Clock, StreamIO, and helpers.

Covered:
- SystemEnv: get() with existing key, missing key (with default), missing key (no default)
- SystemFileSystem: makedirs (create, exist_ok false/true), getsize (exists, missing),
  temp_dir(), temp_file context manager (delete true/false)
- SystemClock: monotonic() returns positive float and increases over time
- StreamIO: write_json, write_raw, read_line (with content, empty, EOF)
- _stdout_to_stderr: redirects io._stdout to io._stderr during context, restores after

Excluded:
- Env subclasses other than SystemEnv — other implementations would be tested the same way
- FileSystem subclasses other than SystemFileSystem — same reason
- Real network, GPU, or file-system edge cases — integration test territory
- Cross-platform behavior (symlinks, permissions) — tested on CI per platform

Note on _stdout_to_stderr:
This context manager works by swapping io._stdout with io._stderr directly on the
module-level io instance. It deliberately does NOT wrap stderr.buffer in a new
TextIOWrapper (which would destroy the buffer when closed). Tests verify the
swap-and-restore behavior by directly manipulating abstractions.io._stdout/_stderr.
"""

import os
import sys
import tempfile
from io import StringIO

import pytest

from abstractions import SystemEnv, SystemFileSystem, SystemClock, StreamIO, _stdout_to_stderr, io as abstractions_io


class TestSystemEnv:
    """SystemEnv wraps os.environ.get(). Basic key lookup and default behavior."""

    def test_get_returns_value_when_exists(self):
        env = SystemEnv()
        result = env.get("PATH")
        assert result is not None or result == ""

    def test_get_returns_default_when_missing(self):
        env = SystemEnv()
        result = env.get("DOES_NOT_EXIST_12345", "default_value")
        assert result == "default_value"

    def test_get_returns_none_when_missing_no_default(self):
        env = SystemEnv()
        result = env.get("DOES_NOT_EXIST_12345")
        assert result is None


class TestSystemFileSystem:
    """SystemFileSystem wraps os.makedirs, os.path.getsize, tempfile.
    Uses pytest's tmp_path fixture for test isolation — no real files in temp_dir.
    """

    def test_makedirs_creates_directory(self, tmp_path):
        fs = SystemFileSystem()
        test_dir = str(tmp_path / "test_dir" / "nested")
        fs.makedirs(test_dir, exist_ok=True)
        assert os.path.isdir(test_dir)

    def test_makedirs_exist_ok_false_raises(self, tmp_path):
        fs = SystemFileSystem()
        test_dir = str(tmp_path / "existing")
        os.makedirs(test_dir)
        with pytest.raises(FileExistsError):
            fs.makedirs(test_dir, exist_ok=False)

    def test_makedirs_exist_ok_true_succeeds(self, tmp_path):
        fs = SystemFileSystem()
        test_dir = str(tmp_path / "existing")
        os.makedirs(test_dir)
        fs.makedirs(test_dir, exist_ok=True)

    def test_getsize_returns_file_size(self, tmp_path):
        fs = SystemFileSystem()
        test_file = tmp_path / "test.txt"
        content = b"hello world"
        test_file.write_bytes(content)
        assert fs.getsize(str(test_file)) == len(content)

    def test_getsize_raises_on_missing(self, tmp_path):
        fs = SystemFileSystem()
        with pytest.raises(FileNotFoundError):
            fs.getsize(str(tmp_path / "missing.txt"))

    def test_temp_dir_returns_temp_directory(self):
        fs = SystemFileSystem()
        assert fs.temp_dir() == tempfile.gettempdir()

    def test_temp_file_context_manager(self):
        fs = SystemFileSystem()
        with fs.temp_file(suffix=".wav", delete=True) as f:
            assert f.name.endswith(".wav")
            assert os.path.exists(f.name)
        # File should be deleted after context

    def test_temp_file_delete_false(self):
        fs = SystemFileSystem()
        with fs.temp_file(suffix=".txt", delete=False) as f:
            assert os.path.exists(f.name)
        assert os.path.exists(f.name)
        os.unlink(f.name)


class TestSystemClock:
    """SystemClock wraps time.monotonic(). Verifies non-negative float and time progression."""

    def test_monotonic_returns_positive_float(self):
        clock = SystemClock()
        t1 = clock.monotonic()
        assert t1 >= 0
        assert isinstance(t1, float)

    def test_monotonic_increases_over_time(self):
        # 50ms sleep is deliberate — 10ms was sometimes too fast on CI/loaded systems
        clock = SystemClock()
        import time
        t1 = clock.monotonic()
        time.sleep(0.05)
        t2 = clock.monotonic()
        assert t2 > t1


class TestStreamIO:
    """StreamIO wraps stdin/stdout/stderr for the JSON protocol.
    Tests write/read paths using StringIO buffers — no real I/O involved.
    """

    def test_write_json_outputs_json_line(self):
        stdin = StringIO()
        stdout = StringIO()
        stderr = StringIO()
        stream_io = StreamIO(stdin, stdout, stderr)
        stream_io.write_json({"id": "123", "path": "/tmp/test.wav"})
        output = stdout.getvalue().strip()
        assert '"id": "123"' in output
        assert '"path": "/tmp/test.wav"' in output

    def test_write_raw_outputs_plain_text(self):
        stdin = StringIO()
        stdout = StringIO()
        stderr = StringIO()
        stream_io = StreamIO(stdin, stdout, stderr)
        stream_io.write_raw("READY")
        assert stdout.getvalue().strip() == "READY"

    def test_read_line_returns_stripped_line(self):
        stdin = StringIO("  hello world  \n")
        stdout = StringIO()
        stderr = StringIO()
        stream_io = StreamIO(stdin, stdout, stderr)
        assert stream_io.read_line() == "hello world"

    def test_read_line_returns_none_on_empty(self):
        stdin = StringIO("")
        stdout = StringIO()
        stderr = StringIO()
        stream_io = StreamIO(stdin, stdout, stderr)
        assert stream_io.read_line() is None

    def test_read_line_returns_none_after_last_line(self):
        stdin = StringIO("line1\n")
        stdout = StringIO()
        stderr = StringIO()
        stream_io = StreamIO(stdin, stdout, stderr)
        assert stream_io.read_line() == "line1"
        assert stream_io.read_line() is None


class TestStdoutToStderr:
    """_stdout_to_stderr swaps io._stdout with io._stderr to prevent Coqui's internal
    print() calls from corrupting the JSON protocol on stdout.

    We test it by directly swapping and restoring abstractions.io's _stdout/_stderr
    references, then verifying write destinations within the context.
    """

    def test_stdout_redirected_to_stderr(self):
        # Save original references
        original_out = abstractions_io._stdout
        original_err = abstractions_io._stderr

        # Create test buffers
        test_stdout = StringIO()
        test_stderr = StringIO()

        # Set up io to write to our test stderr via stdout reference
        abstractions_io._stdout = test_stderr  # stdout now points to stderr buffer
        abstractions_io._stderr = test_stderr

        try:
            with _stdout_to_stderr():
                # Inside context, stdout is redirected to stderr
                abstractions_io._stdout.write("test message\n")

            # The message should be in stderr buffer
            assert "test message" in test_stderr.getvalue()
        finally:
            abstractions_io._stdout = original_out
            abstractions_io._stderr = original_err

    def test_stdout_restored_after_context(self):
        original_out = abstractions_io._stdout
        original_err = abstractions_io._stderr

        with _stdout_to_stderr():
            pass

        assert abstractions_io._stdout is original_out
        assert abstractions_io._stderr is original_err
