"""
TTS Engine: TTSEngine interface and CoquiTTSEngine implementation.
Does not depend on abstractions.py — receives them via constructor.
"""

import logging
import os
from abc import ABC, abstractmethod


# ── TTS Engine abstraction ──────────────────────────────────────────────────────
class TTSEngine(ABC):
    @abstractmethod
    def tts_to_file(self, text: str, file_path: str, voice: str | None = None, model: str | None = None) -> None:
        ...

    @abstractmethod
    def is_ready(self) -> bool:
        ...


class CoquiTTSEngine(TTSEngine):
    def __init__(self, model_name: str | None, model_path: str | None, tts_config_path: str | None, clock, log: logging.Logger) -> None:
        self._tts: object | None = None
        self._model_name = model_name
        self._model_path = model_path
        self._tts_config_path = tts_config_path
        self._clock = clock
        self._log = log

    def is_ready(self) -> bool:
        return self._tts is not None

    def _load(self) -> None:
        if self._tts is not None:
            return
        if not self._model_name and not self._model_path:
            return

        import torch
        from TTS.api import TTS

        can_use_cuda = torch.cuda.is_available()
        self._log.info(f"CUDA available: {can_use_cuda}")

        device = "cuda" if can_use_cuda else "cpu"
        t0 = self._clock.monotonic()

        # model_path (local checkpoint file) takes priority over model_name (HuggingFace ID)
        use_model_path = bool(self._model_path)

        if use_model_path:
            # Validate: model_path must be a file (e.g. model.pth), not a folder
            if not os.path.isfile(self._model_path):
                raise ValueError(
                    f"TTS_MODEL_PATH is not a file: {self._model_path!r}. "
                    f"model_path must point to the model checkpoint file (e.g. model.pth), not a folder. "
                    f"Use TTS_CONFIG_PATH to specify the config.json separately."
                )
            if not self._tts_config_path or not os.path.isfile(self._tts_config_path):
                raise ValueError(
                    f"TTS_CONFIG_PATH is missing or not a file: {self._tts_config_path!r}. "
                    f"When using a local model (TTS_MODEL_PATH), you must also set TTS_CONFIG_PATH "
                    f"pointing to the config.json file."
                )
            self._log.info(f"Loading local model — model_path={self._model_path!r}, config_path={self._tts_config_path!r}")

        try:
            if not can_use_cuda:
                raise RuntimeError("CUDA not available")

            if use_model_path:
                self._tts = TTS(model_path=self._model_path, config_path=self._tts_config_path, progress_bar=False).to(device)
            else:
                self._tts = TTS(model_name=self._model_name, progress_bar=False).to(device)
            self._log.info(f"TTS loaded on CUDA in {self._clock.monotonic() - t0:.1f}s")

        except Exception as cuda_err:
            self._log.warning("CUDA unavailable, using CPU")
            if can_use_cuda:
                try:
                    torch.cuda.empty_cache()
                except Exception:
                    pass

            t0 = self._clock.monotonic()
            if use_model_path:
                self._tts = TTS(model_path=self._model_path, config_path=self._tts_config_path, progress_bar=False, gpu=False)
            else:
                self._tts = TTS(model_name=self._model_name, progress_bar=False, gpu=False)
            self._log.info(f"TTS loaded on CPU in {self._clock.monotonic() - t0:.1f}s")

        # TTS 0.22.0 with local model_path: config.languages may not exist on
        # VitsConfig, and is_multi_lingual property getter crashes on that missing
        # attribute. Patch config.languages = [] to prevent the crash.
        if self._tts.config is not None and not hasattr(self._tts.config, "languages"):
            object.__setattr__(self._tts.config, "languages", [])

        # TTS 0.22.0 with local model_path doesn't set is_multi_lingual
        # via the normal property path. Set it directly on the config object
        # so subsequent property access via self.config.languages works.
        if self._tts.config is not None and not hasattr(self._tts, "is_multi_lingual"):
            object.__setattr__(self._tts, "is_multi_lingual", False)

        loaded_from = self._model_path if use_model_path else self._model_name
        self._log.info(f"Model loaded successfully: {loaded_from}")

    def tts_to_file(self, text: str, file_path: str, voice: str | None = None, model: str | None = None) -> None:
        self._load()
        if self._tts is None:
            raise RuntimeError("No TTS model loaded")
        # voice maps to speaker_wav for multi-speaker models (e.g. xtts);
        # model param is available for future runtime switching but requires
        # reloading the engine, so for now it is accepted but ignored here.
        # Only pass speaker_wav when non-None — TTS 0.22.0 _check_arguments
        # accesses is_multi_lingual even when speaker_wav=None, which fails
        # for models loaded from local path.
        if voice:
            self._tts.tts_to_file(text=text, file_path=file_path, speaker_wav=voice)
        else:
            self._tts.tts_to_file(text=text, file_path=file_path)

    def ensure_ready(self) -> None:
        """Load the model and raise on failure. Safe to call multiple times."""
        self._load()
        if self._tts is None and (self._model_name or self._model_path):
            raise RuntimeError("Failed to load TTS model")
