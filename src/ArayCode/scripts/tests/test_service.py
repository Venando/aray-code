"""
Tests for service.py — request handling and main loop.

Covered:
- handle_request with missing text → sendProtocol({"type": "error", ...})
- handle_request with no TTS model loaded → sendProtocol({"type": "error", ...})
- handle_request with valid request → calls tts.tts_to_file()
- start() sets _started flag and loads CoquiTTSEngine once (idempotent)
- run() main loop: processes JSON lines, handles invalid JSON, stops on EXIT

Excluded — require CUDA or model files, or are environment-specific:
- Real TTS synthesis end-to-end (actual audio file produced) — integration test
- Model loading with real CoquiTTS (torch.cuda, model weights download) — integration test
- Log file creation (logging.FileHandler to disk) — mocked, tested separately in abstractions
- Real clock timing / performance measurement — tested via abstractions
- Network or GPU fallback behavior — integration concerns

Key design decisions:
- service module is imported at class-load time (not inside test functions) so that
  patch.object(service, ...) can patch module-level globals in place.
- patching sys.stdin directly for run() tests bypasses pytest's stdin capture.
- abstractions.io._stdout is patched directly because StreamIO stores the reference
  at instantiation time — patching sys.stdout does not affect an already-created StreamIO.
- All protocol messages now use sendProtocol({"type": "..."}) — the "type" field drives C# dispatch.
"""

import pytest
from unittest.mock import MagicMock, patch
import sys
from io import StringIO


# Module-level import so we can reference service in all tests
import service


class TestHandleRequest:
    """Tests for handle_request function."""

    def test_missing_text_returns_error(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(service, "tts", None), \
             patch.object(service, "sendProtocol") as mock_sendProtocol:

            service.handle_request({"id": "test123"})

            mock_sendProtocol.assert_called_once()
            call_args = mock_sendProtocol.call_args[0][0]
            assert call_args["type"] == "error"
            assert call_args["id"] == "test123"
            assert "text" in call_args["msg"].lower()

    def test_no_tts_model_returns_error(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(service, "tts", None), \
             patch.object(service, "sendProtocol") as mock_sendProtocol:

            service.handle_request({"id": "test456", "text": "Hello world"})

            mock_sendProtocol.assert_called_once()
            call_args = mock_sendProtocol.call_args[0][0]
            assert call_args["type"] == "error"
            assert call_args["id"] == "test456"

    def test_valid_request_calls_tts(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log, tmp_path):
        mock_tts = MagicMock()
        temp_wav = tmp_path / "output.wav"
        mock_fs._files[str(temp_wav)] = b"fake audio data"
        mock_fs._temp_dir = str(tmp_path)

        mock_temp_file = MagicMock()
        mock_temp_file.name = str(temp_wav)
        mock_temp_file.__enter__ = MagicMock(return_value=mock_temp_file)
        mock_temp_file.__exit__ = MagicMock(return_value=False)

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(service, "tts", mock_tts), \
             patch("tempfile.NamedTemporaryFile", return_value=mock_temp_file):

            service.handle_request({"id": "test789", "text": "Hello world"})
            mock_tts.tts_to_file.assert_called_once()

    def test_valid_request_forwards_voice_and_model(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        """Verify voice and model from the request are forwarded to tts_to_file."""
        mock_tts = MagicMock()
        # mock_fs generates temp paths like temp_1.wav, temp_2.wav internally
        mock_fs._temp_dir = "/tmp"
        expected_path = "/tmp/temp_1.wav"
        mock_fs._files[expected_path] = b"fake audio data"

        mock_temp_file = MagicMock()
        mock_temp_file.name = expected_path
        mock_temp_file.__enter__ = MagicMock(return_value=mock_temp_file)
        mock_temp_file.__exit__ = MagicMock(return_value=False)

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(service, "tts", mock_tts), \
             patch("tempfile.NamedTemporaryFile", return_value=mock_temp_file):

            service.handle_request({"id": "req_voice", "text": "Hello", "voice": "/path/to/ref.wav", "model": "my_model"})
            mock_tts.tts_to_file.assert_called_once_with(
                text="Hello",
                file_path=expected_path,
                voice="/path/to/ref.wav",
                model="my_model",
            )


class TestStart:
    """Tests for start function."""

    def test_start_sets_started_flag(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        # Reset state
        service._started = False
        service.tts = None

        mock_env._values["TTS_MODEL"] = "tts_models/en/ljspeech/vits"

        mock_engine = MagicMock()
        mock_engine.ensure_ready = MagicMock()

        # Mock FileHandler to avoid needing real log directory on disk
        mock_file_handler = MagicMock()
        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(service, "CoquiTTSEngine", return_value=mock_engine), \
             patch("logging.FileHandler", return_value=mock_file_handler):

            service.start()

            assert service._started is True
            assert service.tts is mock_engine
            mock_engine.ensure_ready.assert_called_once()

    def test_start_idempotent(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        service._started = True
        existing_tts = MagicMock()
        service.tts = existing_tts

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log):

            service.start()
            assert service._started is True
            assert service.tts is existing_tts


class TestRun:
    """Tests for the main run loop — patches sys.stdin directly."""

    def test_run_processes_json_lines(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        service._started = True
        service.tts = MagicMock()

        test_input = StringIO('{"id": "req1", "text": "Hello"}\n{"id": "req2", "text": "World"}\nEXIT\n')
        captured_stdout = StringIO()

        # sendProtocol uses abstractions.io (captured at import time), not service.io.
        # Patch abstractions.io._stdout directly so print() output goes to our buffer.
        import abstractions
        orig_stdout = abstractions.io._stdout

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(sys, "stdin", test_input):

            abstractions.io._stdout = captured_stdout
            try:
                service.run()
            finally:
                abstractions.io._stdout = orig_stdout

            # Protocol now uses sendProtocol({"type": "ok", "id": "...", "path": "..."})
            output = captured_stdout.getvalue()
            assert '"type": "ok"' in output
            assert '"id": "req1"' in output
            assert '"id": "req2"' in output

    def test_run_handles_invalid_json(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        service._started = True
        service.tts = MagicMock()

        test_input = StringIO('not valid json {\nEXIT\n')
        captured_stdout = StringIO()

        # Patch abstractions.io._stdout directly — StreamIO stores reference at instantiation,
        # so patching sys.stdout after io is created has no effect on io._stdout.
        import abstractions
        orig_stdout = abstractions.io._stdout

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(sys, "stdin", test_input):

            abstractions.io._stdout = captured_stdout
            try:
                service.run()
            finally:
                abstractions.io._stdout = orig_stdout

            output = captured_stdout.getvalue()
            assert "Invalid JSON" in output

    def test_run_stops_on_exit(self, mock_env, mock_fs, mock_clock, mock_stream, mock_log):
        service._started = True
        service.tts = MagicMock()

        test_input = StringIO('{"id": "req1", "text": "Hello"}\nEXIT\n{"id": "req2", "text": "Should not process"}\n')
        captured_stdout = StringIO()

        import abstractions
        orig_stdout = abstractions.io._stdout

        with patch.object(service, "env", mock_env), \
             patch.object(service, "fs", mock_fs), \
             patch.object(service, "clock", mock_clock), \
             patch.object(service, "io", mock_stream), \
             patch.object(service, "log", mock_log), \
             patch.object(sys, "stdin", test_input):

            abstractions.io._stdout = captured_stdout
            try:
                service.run()
            finally:
                abstractions.io._stdout = orig_stdout

            # Protocol now uses sendProtocol({"type": "ok", ...})
            output = captured_stdout.getvalue()
            assert '"id": "req1"' in output
            # req2 should not be processed (EXIT stops the loop)
            assert "req2" not in output
