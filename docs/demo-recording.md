# Запись демо: сцены 2, 8, 9

## Сборки

```bash
tools/publish-demo.sh osx-arm64   # -> dist/osx-arm64/SWINE.app (без подписи)
tools/publish-demo.sh win-x64     # -> dist/win-x64/SWINE.exe, сервер в server/Backend.exe
tools/publish-demo.sh linux-x64   # -> dist/linux-x64/SWINE + dist/SWINE-linux-x64.tar.gz (права сохраняются)
```

На Windows: `pwsh tools/publish-demo.ps1 -Rid win-x64`. Windows-zip не хранит бит исполняемости: для Linux раздавайте tar.gz.

## Сцена 2: запуск из бандла

macOS: приложение не подписано и не нотаризовано. Перед записью один раз: правый клик по `SWINE.app` -> «Открыть» -> «Открыть»
(либо `xattr -dr com.apple.quarantine SWINE.app`, если архив скачан). Дальше запускается двойным кликом.
Windows: `SWINE.exe` (SmartScreen: «Подробнее» -> «Выполнить в любом случае»). Linux: `tar xzf SWINE-linux-x64.tar.gz && ./SWINE`.

Клиент сам поднимает локальный сервер из `server/` на свободном порту и гасит его при выходе.
Проверка без UI: `SWINE --launcher-check 20` (путь к бинарю внутри .app: `SWINE.app/Contents/MacOS/SWINE`).
Лог сервера: папка данных клиента, `logs/server.log`.

## Сцена 8: подмена ключа

SWINE закрыт (сервер читает known_hosts при старте и перезаписывает файл целиком):

```bash
tools/demo-fake-hostkey.sh <host> <port>             # Windows: pwsh tools/demo-fake-hostkey.ps1 <host> <port>
```

Запустите SWINE и подключитесь к этому host:port: появится диалог о несовпадении ключа, каждый раз.
Реальный SSH-сервер не меняется. После записи вернуть как было (SWINE закрыт):

```bash
tools/demo-fake-hostkey.sh --restore <host> <port>   # Windows: ... -Restore
```

Файл: `known_hosts.json` в `{AppData}/RemoteFileManager/` (macOS `~/Library/Application Support`, Linux `~/.config`,
Windows `%APPDATA%`) либо в `$REMOTE_FILE_MANAGER_DATA_DIR/RemoteFileManager/`.

## Сцена 9: сервер синхронизации

На VPS с Docker и доменом (A-запись -> VPS, порты 80/443): см. `deploy/sync-server/README.md`.

1. `cd deploy/sync-server && echo "SWINE_DOMAIN=sync.example.com" > .env && docker compose up -d`
2. В SWINE: «Синхронизация» -> адрес `https://sync.example.com`.

Первая выдача сертификата занимает до минуты. Данные в томе `swine-data`; сброс: `docker compose down -v`.
