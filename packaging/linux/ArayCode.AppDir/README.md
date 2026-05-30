# Linux AppImage Packaging for ArayCode

This directory contains the structure for building an AppImage of ArayCode.

## Directory Structure

```
ArayCode.AppDir/
├── AppRun              # Launcher script that sets up PATH
├── aray-code.desktop   # Desktop entry file
├── aray-code.png       # Icon (256x256 PNG - you need to add this)
└── usr/
    └── bin/
        └── aray-code   # Symlink to actual binary (created during build)
```

## Build Instructions

1. Publish self-contained Linux binary:
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish/linux
   ```

2. Copy published files to AppDir:
   ```bash
   cp -r ./publish/linux/* ArayCode.AppDir/usr/bin/
   ```

3. Create symlink for 'aray' alias:
   ```bash
   ln -sf aray-code ArayCode.AppDir/usr/bin/aray
   ```

4. Download appimagetool (if not present):
   ```bash
   wget -O appimagetool-x86_64.AppImage \
     "https://github.com/AppImage/AppImageKit/releases/latest/download/appimagetool-x86_64.AppImage"
   chmod +x appimagetool-x86_64.AppImage
   ```

5. Build the AppImage:
   ```bash
   ./appimagetool-x86_64.AppImage ArayCode.AppDir aray-code.AppImage
   ```

## Installation

Users can install via:
```bash
sudo cp aray-code.AppImage /usr/local/bin/aray
sudo chmod +x /usr/local/bin/aray
```

This installs as `aray` (the alias) so users can type `aray` from anywhere.

## Notes

- You must add a256x256 PNG icon named `aray-code.png` to this directory before building
- The AppRun script sets up the PATH so both `aray` and `aray-code` work
- The alias `aray` is created as a symlink to `aray-code`
