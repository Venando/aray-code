"""
Tests for engine.py — TTSEngine interface and CoquiTTSEngine implementation.

Covered:
- TTSEngine abstract interface (cannot be instantiated directly)
- CoquiTTSEngine state transitions (not ready → ready)
- Constructor field storage (_log, _clock, _model_name)
- _load() delegation from tts_to_file()

Excluded — require CUDA or model files, or are architecture-specific:
- Real TTS model loading (torch/TTS instantiation) — integration test territory
- CUDA fallback to CPU behavior — requires CoquiTTS installed with GPU support
- Actual audio synthesis — requires model weights and TTS runtime

We test the contract: that ensure_ready() and tts_to_file() call _load(),
and that is_ready() correctly reflects the loaded state. The actual Coqui
initialization is a separate integration concern.
"""

import pytest
from unittest.mock import MagicMock, patch


class TestTTSEngineInterface:
    """Tests for the abstract TTSEngine interface."""

    def test_tts_engine_is_abstract(self):
        from engine import TTSEngine
        with pytest.raises(TypeError):
            TTSEngine()


class TestCoquiTTSEngine:
    """Tests for CoquiTTSEngine — unit tests that mock TTS library."""

    def test_is_ready_returns_false_when_not_loaded(self, mock_clock, mock_log):
        from engine import CoquiTTSEngine
        engine = CoquiTTSEngine("test_model", None, None, mock_clock, mock_log)
        assert engine.is_ready() is False

    def test_is_ready_returns_true_after_load(self, mock_clock, mock_log):
        from engine import CoquiTTSEngine
        mock_tts_instance = MagicMock()
        engine = CoquiTTSEngine("test_model", None, None, mock_clock, mock_log)
        engine._tts = mock_tts_instance
        assert engine.is_ready() is True

    def test_ensure_ready_with_model_name_none_does_not_raise(self, mock_clock, mock_log):
        from engine import CoquiTTSEngine
        engine = CoquiTTSEngine(None, None, None, mock_clock, mock_log)
        engine.ensure_ready()
        assert engine._tts is None

    def test_log_and_clock_stored_correctly(self, mock_clock, mock_log):
        from engine import CoquiTTSEngine
        engine = CoquiTTSEngine("my_model", None, None, mock_clock, mock_log)
        assert engine._log is mock_log
        assert engine._clock is mock_clock
        assert engine._model_name == "my_model"

    def test_tts_to_file_calls_load(self, mock_clock, mock_log):
        """Verify tts_to_file calls _load() — the actual TTS load is integration-tested separately."""
        from engine import CoquiTTSEngine
        engine = CoquiTTSEngine("test_model", None, None, mock_clock, mock_log)
        engine._load = MagicMock()
        try:
            engine.tts_to_file("hello", "/tmp/test.wav")
        except Exception:
            pass  # May fail if TTS not installed, but _load should have been called
        engine._load.assert_called_once()

    def test_tts_to_file_passes_voice_to_underlying_tts(self, mock_clock, mock_log):
        """Verify voice is forwarded as speaker_wav to the underlying TTS library."""
        from engine import CoquiTTSEngine
        mock_tts_instance = MagicMock()
        engine = CoquiTTSEngine("test_model", None, None, mock_clock, mock_log)
        engine._tts = mock_tts_instance
        engine.tts_to_file("hello", "/tmp/test.wav", voice="/path/to/ref.wav")
        mock_tts_instance.tts_to_file.assert_called_once_with(
            text="hello",
            file_path="/tmp/test.wav",
            speaker_wav="/path/to/ref.wav",
        )
