
# <img src="https://github.com/user-attachments/assets/0f03335a-787a-4404-934f-7648aba9010b" width="48" align="middle"> <sub>Aray Code</sub>

It's [OpenClaw](https://github.com/openclaw/openclaw) Node desktop CLI app, inspired by Claude Code, Kimi Code and other CLI apps.


Built-in TTS and STT. Multi agent management.

https://github.com/user-attachments/assets/70ef031a-cc58-46d8-832f-04d353a6e29c

## Quick Start

```bash
dotnet run
```

## Installation (Windows)

Download the latest release from the [Releases](https://github.com/Venando/aray-code/releases) page and run `ArayCode-Setup.exe`. Follow the installer prompts — it will add Aray Code to your Start Menu and PATH.

#### Run

```powershell
aray
```

Or launch from the Start Menu shortcut.

---

## Installation (Linux)

Download the latest release from the [Releases](https://github.com/Venando/aray-code/releases) page.

### Option 1: tar.gz — portable, no dependencies (recommended)

#### Download, extract, install & clean up — one command

```bash
curl -L https://github.com/Venando/aray-code/releases/latest/download/aray.tar.gz | tar xz && bash aray/setup.sh && rm -rf aray/
```

#### Run
```bash
aray
```


#### Uninstall

```bash
bash ~/.local/share/aray-code/uninstall-aray.sh
```

### Option 2: AppImage — single file, needs FUSE

#### Download
```bash
wget https://github.com/Venando/aray-code/releases/latest/download/aray.AppImage
```

#### Install to PATH
```bash
sudo install -m 755 aray.AppImage /usr/local/bin/aray
```

#### Run
```bash
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

- **Multi-agent**: Per-agent hotkeys and other settings via `agents.json` and `config.json`
- **Speach-to-Text**: Cloud: Groq, OpenAI or Local: Whisper.cpp (automanaged)
- **Text-to-Speach**: Cloud: OpenAI, Edge (not tested) or Local Coqui TTS, Supertonic 3 (automanaged), Piper (not tested)
- **Text input**: Ctrl, Home, End, Shift, Arrows, (Ctrl +Z +X +C +A) <- All of this works in any combination like in normal text editor
- **Cross-platform**: Windows, Linux, ~~macOS~~

## License

[MIT](LICENSE)
