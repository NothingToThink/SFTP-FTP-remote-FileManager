#!/usr/bin/env bash
# Собирает демо-бандл: клиент + сервер, self-contained, в dist/<rid>/ (сервер в dist/<rid>/server/).
# Использование: tools/publish-demo.sh [osx-arm64|osx-x64|linux-x64|win-x64]
# osx-*: на выходе dist/<rid>/SWINE.app (без подписи); linux-*: ещё и dist/SWINE-<rid>.tar.gz
# Без аргумента RID определяется по текущей машине. Запуск: dist/<rid>/SWINE[.exe]
set -euo pipefail
cd "$(dirname "$0")/.."

rid="${1:-}"
if [ -z "$rid" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) rid=osx-arm64 ;;
    Darwin-x86_64) rid=osx-x64 ;;
    Linux-x86_64) rid=linux-x64 ;;
    Linux-aarch64) rid=linux-arm64 ;;
    *) echo "Не удалось определить RID, передайте аргументом" >&2; exit 1 ;;
  esac
fi

out="dist/$rid"
rm -rf "$out"
mkdir -p "$out"

echo "== Backend -> $out/server"
dotnet publish Backend/Backend.csproj -c Release -r "$rid" --self-contained true -o "$out/server" --nologo

echo "== Client -> $out"
dotnet publish Client/FileManagerClient.csproj -c Release -r "$rid" --self-contained true -o "$out" --nologo

# Переименовываем apphost: он находит FileManagerClient.dll по встроенному имени, так что имя файла не важно
ext=""; case "$rid" in win-*) ext=".exe" ;; esac
mv "$out/FileManagerClient$ext" "$out/SWINE$ext"

# Права: на Linux/macOS оба apphost должны быть исполняемыми (tar.gz их сохраняет, zip - нет)
case "$rid" in win-*) ;; *) chmod +x "$out/SWINE$ext" "$out/server/Backend" ;; esac

case "$rid" in
  osx-*)
    # SWINE.app: всё содержимое публикации лежит в Contents/MacOS, сервер - в Contents/MacOS/server,
    # поэтому ServerLocator (AppContext.BaseDirectory/server) находит его без изменений.
    app="$out/SWINE.app"
    macos="$app/Contents/MacOS"
    res="$app/Contents/Resources"
    mkdir -p "$macos" "$res"
    shopt -s dotglob
    for f in "$out"/*; do
      [ "$f" = "$app" ] && continue
      mv "$f" "$macos/"
    done
    shopt -u dotglob

    icon_src="Client/Assets/AppIcon/app-icon.png"
    iconset="$(mktemp -d)/SWINE.iconset"
    mkdir -p "$iconset"
    for sz in 16 32 128 256 512; do
      sips -z $sz $sz "$icon_src" --out "$iconset/icon_${sz}x${sz}.png" >/dev/null
      sips -z $((sz*2)) $((sz*2)) "$icon_src" --out "$iconset/icon_${sz}x${sz}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$res/SWINE.icns"
    rm -rf "$(dirname "$iconset")"

    cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SWINE</string>
  <key>CFBundleDisplayName</key><string>SWINE</string>
  <key>CFBundleIdentifier</key><string>dev.swine.filemanager</string>
  <key>CFBundleExecutable</key><string>SWINE</string>
  <key>CFBundleIconFile</key><string>SWINE</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    echo "Готово: $app (без подписи: первый запуск - правый клик -> Открыть)"
    ;;
  linux-*)
    tar -C "$out" -czf "dist/SWINE-$rid.tar.gz" .
    echo "Готово: $out/SWINE$ext, архив dist/SWINE-$rid.tar.gz (сохраняет права)"
    ;;
  *)
    echo "Готово: $out/SWINE$ext"
    ;;
esac
