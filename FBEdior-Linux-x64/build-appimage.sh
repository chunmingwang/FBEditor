#!/usr/bin/env bash
# Build FBEditor as a Linux AppImage.
# Run from the project root (~/NETCore/fbeditor-port), with fbeditor.png alongside this script.
set -e

APP=FBEditor.Avalonia
APPDIR=AppDir
ICON=fbeditor.png

# 1. Publish self-contained (NOT single-file: keeps native .so files flat next to the
#    executable, where SkiaSharp/Avalonia resolve them. Single-file hides them and breaks
#    libSkiaSharp loading inside an AppImage).
dotnet publish "$APP" -c Release -r linux-x64 --self-contained true
PUBLISH="$APP/bin/Release/net10.0/linux-x64/publish"

# 2. Assemble the AppDir.
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH"/* "$APPDIR/usr/bin/"

# Icon at AppDir root + hicolor.
cp "$ICON" "$APPDIR/fbeditor.png"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp "$ICON" "$APPDIR/usr/share/icons/hicolor/256x256/apps/fbeditor.png"

# Desktop entry.
cat > "$APPDIR/FBEditor.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=FBEditor
Comment=FreeBASIC IDE
Exec=FBEditor.Avalonia
Icon=fbeditor
Categories=Development;IDE;
Terminal=false
DESK
mkdir -p "$APPDIR/usr/share/applications"
cp "$APPDIR/FBEditor.desktop" "$APPDIR/usr/share/applications/"

# AppRun: ensure the native libs next to the exe are on the loader path, then run.
cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/bin:$LD_LIBRARY_PATH"
exec "$HERE/usr/bin/FBEditor.Avalonia" "$@"
RUN
chmod +x "$APPDIR/AppRun"

# 3. appimagetool (reused if already present in this folder).
if [ ! -x ./appimagetool-x86_64.AppImage ]; then
  wget -O appimagetool-x86_64.AppImage \
    https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x appimagetool-x86_64.AppImage
fi

# 4. Package.
ARCH=x86_64 ./appimagetool-x86_64.AppImage --appimage-extract-and-run "$APPDIR" FBEditor-x86_64.AppImage

echo
echo "Done -> FBEditor-x86_64.AppImage"
echo "Run with:  ./FBEditor-x86_64.AppImage"
