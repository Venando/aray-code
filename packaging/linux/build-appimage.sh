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

echo "=== ArayCode Build Script ==="

# Step 1: Clean and prepare
echo "[1/6] Cleaning previous build..."
rm -rf "$APPDIR/usr/bin/"* 2>/dev/null || true
rm -rf "$PUBLISH_DIR/"* 2>/dev/null || true
rm -f "$SCRIPT_DIR/aray.AppImage" 2>/dev/null || true
rm -f "$SCRIPT_DIR/aray.tar.gz" 2>/dev/null || true

# Step 2: Publish .NET app (main project only, not tests)
echo "[2/6] Publishing .NET app for Linux..."
cd "$SCRIPT_DIR/../.."
dotnet publish src/ArayCode/ArayCode.csproj -f net10.0 -c Release -r linux-x64 --self-contained true -o "$PUBLISH_DIR"

# Step 3: Copy published files to AppDir
echo "[3/6] Copying files to AppDir..."
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR/"* "$APPDIR/usr/bin/"

# Step 4: Create 'aray' symlink alias
echo "[4/6] Creating 'aray' alias symlink..."
cd "$APPDIR/usr/bin"
if [ -f "aray-code" ]; then
    ln -sf aray-code aray
    echo "  Created symlink: aray -> aray-code"
else
    echo " WARNING: aray-code not found in publish directory"
    ls -la
fi

# Step 5: Build AppImage
echo "[5/6] Building AppImage..."

# Download appimagetool if not present
if [ ! -f "$APPIMAGE_TOOL" ]; then
    echo "  Downloading appimagetool..."
    wget -O "$APPIMAGE_TOOL" \
 "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$APPIMAGE_TOOL"
fi

cd "$SCRIPT_DIR"
chmod +x "$APPIMAGE_TOOL"
chmod +x "$APPDIR/AppRun"
# Use APPIMAGE_EXTRACT_AND_RUN=1 to support systems without FUSE
APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGE_TOOL" "$APPDIR" aray.AppImage

# Step 6: Create tar.gz archive with install script (portable, no FUSE needed)
echo "[6/6] Creating tar.gz archive..."
STAGING_DIR="$SCRIPT_DIR/aray-staging"
mkdir -p "$STAGING_DIR"
cp -r "$PUBLISH_DIR/"* "$STAGING_DIR/"
ln -sf aray-code "$STAGING_DIR/aray"

# Add desktop entry and icon for app launcher
cp "$APPDIR/aray-code.desktop" "$STAGING_DIR/"
cp "$APPDIR/aray-code.png" "$STAGING_DIR/"

# Create a setup script
cat > "$STAGING_DIR/setup.sh" << 'SETUP_EOF'
#!/bin/bash
# ArayCode Linux setup script
# Usage: bash setup.sh
# Copies app files to ~/.local/share/aray-code/,
# creates a symlink in ~/.local/bin/, and installs the desktop entry.

set -e

APP_DIR="$HOME/.local/share/aray-code"
BIN_DIR="$HOME/.local/bin"
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "Installing ArayCode..."

# Create target directories
mkdir -p "$APP_DIR" "$BIN_DIR"

# Copy all app files to the dedicated directory
cp -r "$SOURCE_DIR/"* "$APP_DIR/"
rm -f "$APP_DIR/setup.sh"

# Create symlink in PATH
ln -sf "$APP_DIR/aray" "$BIN_DIR/aray"

# Install desktop entry and icon
mkdir -p "$HOME/.local/share/applications" "$HOME/.local/share/icons"
cat > "$HOME/.local/share/applications/aray-code.desktop" << DESKTOP_EOF
[Desktop Entry]
Name=ArayCode
Comment=Code analysis and transformation tool
Exec=$BIN_DIR/aray
Icon=aray-code
Terminal=true
Type=Application
Categories=Development;
Keywords=code;aray;analysis;
DESKTOP_EOF

cp "$APP_DIR/aray-code.png" "$HOME/.local/share/icons/"
update-desktop-database "$HOME/.local/share/applications/" 2>/dev/null || true

echo ""
echo "============================================"
echo " ArayCode installed!"
echo "============================================"
echo ""
echo "  Run: aray"
echo ""

# Ensure BIN_DIR is in PATH
case ":$PATH:" in
  *:"$BIN_DIR":*) ;;
  *)
    echo "  Add to your ~/.bashrc to always have 'aray' available:"
    echo "    echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc"
    ;;
esac

echo ""
echo "  To uninstall later: bash $APP_DIR/uninstall-aray.sh"
SETUP_EOF
chmod +x "$STAGING_DIR/setup.sh"

# Create an uninstall script
cat > "$STAGING_DIR/uninstall-aray.sh" << 'UNINSTALL_EOF'
#!/bin/bash
# ArayCode Linux uninstall script
# Removes the symlink, app directory, and desktop entry.

APP_DIR="$HOME/.local/share/aray-code"
BIN_DIR="$HOME/.local/bin"

if [ ! -d "$APP_DIR" ]; then
  echo "Error: ArayCode is not installed in $APP_DIR."
  echo "Nothing was removed."
  exit 1
fi

echo "Removing ArayCode..."

# Remove symlink
rm -f "$BIN_DIR/aray"

# Remove app directory
rm -rf "$APP_DIR"

# Remove desktop entry and icon
rm -f "$HOME/.local/share/applications/aray-code.desktop" \
      "$HOME/.local/share/icons/aray-code.png"
update-desktop-database "$HOME/.local/share/applications/" 2>/dev/null || true

echo ""
echo "ArayCode uninstalled."
UNINSTALL_EOF
chmod +x "$STAGING_DIR/uninstall-aray.sh"

# Re-structure into an aray/ directory for clean extraction
WRAP_DIR="$SCRIPT_DIR/aray-staging-wrap/aray"
mkdir -p "$WRAP_DIR"
cp -r "$STAGING_DIR/"* "$WRAP_DIR/"
cd "$SCRIPT_DIR"
tar czf aray.tar.gz -C "$SCRIPT_DIR/aray-staging-wrap" aray
rm -rf "$STAGING_DIR" "$SCRIPT_DIR/aray-staging-wrap"

echo ""
echo "=== Build Complete ==="
echo "Outputs:"
echo "  $SCRIPT_DIR/aray.AppImage    (AppImage - needs FUSE)"
echo "  $SCRIPT_DIR/aray.tar.gz      (tar.gz - portable, no deps)"
echo ""
echo "Installation (AppImage):"
echo "  sudo cp aray.AppImage /usr/local/bin/aray && sudo chmod +x /usr/local/bin/aray"
echo ""
echo "Installation (tar.gz):"
echo "  tar xzf aray.tar.gz"
echo "  cd aray"
echo "  bash setup.sh"
echo ""
echo "Then users can run: aray"
