#!/bin/bash
# Build script for ArayCode AppImage
# Usage: ./build-appimage.sh [publish-dir]
#
# Requirements:
#   - .NET SDK installed
#   - wget (for downloading appimagetool if not present)
#
# The script creates an AppImage with 'aray' as an alias symlink to aray-code

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPDIR="$SCRIPT_DIR/ArayCode.AppDir"
PUBLISH_DIR="${1:-$SCRIPT_DIR/../../publish/linux}"
APPIMAGE_TOOL="$SCRIPT_DIR/appimagetool-x86_64.AppImage"

echo "=== ArayCode AppImage Build Script ==="

# Step 1: Clean and prepare
echo "[1/5] Cleaning previous build..."
rm -rf "$APPDIR/usr/bin/"* 2>/dev/null || true
rm -f "$SCRIPT_DIR/"*.AppImage 2>/dev/null || true

# Step 2: Publish .NET app
echo "[2/5] Publishing .NET app for Linux..."
cd "$SCRIPT_DIR/../.."
dotnet publish -c Release -r linux-x64 --self-contained true -o "$PUBLISH_DIR"

# Step 3: Copy published files to AppDir
echo "[3/5] Copying files to AppDir..."
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR/"* "$APPDIR/usr/bin/"

# Step 4: Create 'aray' symlink alias
echo "[4/5] Creating 'aray' alias symlink..."
cd "$APPDIR/usr/bin"
if [ -f "aray-code" ]; then
    ln -sf aray-code aray
    echo "  Created symlink: aray -> aray-code"
else
    echo " WARNING: aray-code not found in publish directory"
    ls -la
fi

# Step 5: Build AppImage
echo "[5/5] Building AppImage..."

# Download appimagetool if not present
if [ ! -f "$APPIMAGE_TOOL" ]; then
    echo "  Downloading appimagetool..."
    wget -O "$APPIMAGE_TOOL" \
 "https://github.com/AppImage/AppImageKit/releases/latest/download/appimagetool-x86_64.AppImage"
    chmod +x "$APPIMAGE_TOOL"
fi

cd "$SCRIPT_DIR"
chmod +x "$APPIMAGE_TOOL"
chmod +x "$APPDIR/AppRun"
"$APPIMAGE_TOOL" "$APPDIR" aray.AppImage

echo ""
echo "=== Build Complete ==="
echo "Output: $SCRIPT_DIR/aray.AppImage"
echo ""
echo "Installation:"
echo "  sudo cp aray.AppImage /usr/local/bin/aray"
echo "  sudo chmod +x /usr/local/bin/aray"
echo ""
echo "Then users can run: aray"
