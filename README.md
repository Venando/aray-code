# Aray Code

It's [OpenClaw](https://github.com/openclaw/openclaw) Node desktop CLI app, inspired by Claude Code, Kimi Code and other CLI apps.

<img width="830" height="471" alt="image" src="https://github.com/user-attachments/assets/bd257035-bab4-4e83-959b-764ccc30417a" />

## Quick Start

```bash
dotnet run
```

## Features

- **Multi-agent**: Per-agent hotkeys and other settings via `agents.json`
- **Speach-to-Text**: Cloud: Groq, OpenAI or Local: Whisper.cpp (automanaged)
- **Text-to-Speach**: Cloud: OpenAI, Edge (not tested) or Local Coqui TTS, Supertonic 3 (automanaged), Piper (not tested)
- **Text input**: Ctrl, Home, End, Shift, Arrows, (Ctrl +Z +X +C +A) <- All of this works in any combination like in normal text editor
- **Cross-platform**: Windows (the only tested platform), macOS, Linux
