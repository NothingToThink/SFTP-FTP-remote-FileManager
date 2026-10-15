#!/usr/bin/env bash
# Собирает демо-бандл: клиент + сервер, self-contained, в dist/<rid>/ (сервер в dist/<rid>/server/).
# Использование: tools/publish-demo.sh [osx-arm64|osx-x64|linux-x64|win-x64]
# Без аргумента RID определяется по текущей машине. Запуск: dist/<rid>/FileManagerClient[.exe]
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

echo "Готово: $out"
