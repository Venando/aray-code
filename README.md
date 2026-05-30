# Aray Code

It's [OpenClaw](https://github.com/openclaw/openclaw) Node desktop CLI app, inspired by Claude Code, Kimi Code and other CLI apps.

<img width="830" height="471" alt="image" src="https://github.com/user-attachments/assets/bd257035-bab4-4e83-959b-764ccc30417a" />

## Quick Start

```bash
dotnet run
```

## Installation (Linux)

Download the latest release from the [Releases](https://github.com/Venando/aray-code/releases) page.

### Option 1: tar.gz — portable, no dependencies (recommended)

```bash
# Download, extract, install & clean up — one command
curl -L https://github.com/Venando/aray-code/releases/latest/download/aray.tar.gz | tar xz && bash aray/setup.sh && rm -rf aray/

# Run
aray
```

### Uninstall

```bash
bash ~/.local/share/aray-code/uninstall-aray.sh
```

### Option 2: AppImage — single file, needs FUSE

```bash
# Download
wget https://github.com/Venando/aray-code/releases/latest/download/aray.AppImage

# Install to PATH
sudo install -m 755 aray.AppImage /usr/local/bin/aray

# Run
aray
```

### Building from source

```bash
# Prerequisites: .NET 10.0 SDK

# Clone
git clone https://github.com/Venando/aray-code.git
cd aray-code

# Run directly
dotnet run

# Or build both AppImage + tar.gz
bash packaging/linux/build-appimage.sh
```

## Features

- **Multi-agent**: Per-agent hotkeys and other settings via `agents.json`
- **Speach-to-Text**: Cloud: Groq, OpenAI or Local: Whisper.cpp (automanaged)
- **Text-to-Speach**: Cloud: OpenAI, Edge (not tested) or Local Coqui TTS, Supertonic 3 (automanaged), Piper (not tested)
- **Text input**: Ctrl, Home, End, Shift, Arrows, (Ctrl +Z +X +C +A) <- All of this works in any combination like in normal text editor
- **Cross-platform**: Windows (the only tested platform), macOS, Linux

## License

[MIT](LICENSE)
