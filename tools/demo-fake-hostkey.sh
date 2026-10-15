#!/usr/bin/env bash
# Демо «подмена ключа»: записывает заведомо неверный SHA-256 отпечаток для host:port в known_hosts.json
# локального сервера, так что подключение всегда показывает диалог несовпадения ключа. SSH-сервер не трогается.
# Использование: tools/demo-fake-hostkey.sh <host> <port>
#                tools/demo-fake-hostkey.sh --restore <host> <port>
# Запускать при ЗАКРЫТОМ SWINE: сервер читает файл при старте и перезаписывает его целиком.
# Папка данных: $REMOTE_FILE_MANAGER_DATA_DIR или {AppData}/RemoteFileManager (см. Core/Utils/AppPath.cs).
set -euo pipefail

restore=0
if [ "${1:-}" = "--restore" ]; then restore=1; shift; fi
if [ $# -ne 2 ]; then
  echo "Использование: $0 [--restore] <host> <port>" >&2; exit 2
fi
host="$1"; port="$2"

if [ -n "${REMOTE_FILE_MANAGER_DATA_DIR:-}" ]; then
  base="$REMOTE_FILE_MANAGER_DATA_DIR"
else
  case "$(uname -s)" in
    Darwin) base="$HOME/Library/Application Support" ;;
    *) base="${XDG_CONFIG_HOME:-$HOME/.config}" ;;
  esac
fi
file="$base/RemoteFileManager/known_hosts.json"

python3 - "$file" "$host" "$port" "$restore" <<'PY'
import json, os, sys, base64, datetime
path, host, port, restore = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4] == "1"
key = f"{host.strip().lower()}:{port}"   # KnownHostEntry.KeyFor
entries = []
if os.path.exists(path):
    with open(path, encoding="utf-8") as f:
        entries = json.load(f)
def k(e): return f"{e['Host'].strip().lower()}:{e['Port']}"
before = len(entries)
entries = [e for e in entries if k(e) != key]
if restore:
    print(f"Удалено записей: {before - len(entries)} ({key}) из {path}")
else:
    fake = base64.b64encode(os.urandom(32)).decode().rstrip("=")  # 43 символа, как у OpenSSH SHA256:...
    entries.append({"Host": host, "Port": port, "Algorithm": "ssh-ed25519",
                    "Sha256Fingerprint": fake,
                    "TrustedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat()})
    print(f"Записан неверный отпечаток SHA256:{fake} для {key} в {path}")
os.makedirs(os.path.dirname(path), exist_ok=True)
tmp = path + ".tmp"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(entries, f, indent=2)
os.replace(tmp, path)
PY
