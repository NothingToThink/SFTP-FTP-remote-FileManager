# Sample.Echo

Минимальный пример плагина: команда `sample.echo.ask` спрашивает строку и показывает её
вместе со списком выбранных путей. Ссылается только на `FileManager.Plugins.Sdk`.

## Как опубликовать и подключить

```bash
dotnet publish plugins/Sample.Echo -c Release -o <папка данных>/plugins/sample.echo
```

`<папка данных>` — `AppPaths.GetAppFolder()`: `$REMOTE_FILE_MANAGER_DATA_DIR/RemoteFileManager`, а если переменная
не задана, то `RemoteFileManager` в каталоге данных пользователя (на Linux `~/.config/RemoteFileManager`).
Название папки плагина любое; в ней должны лежать `plugin.json` и `Sample.Echo.dll`.

Копия `FileManager.Plugins.Sdk.dll` в опубликованной папке не мешает: Backend всегда берёт SDK у себя.
Плагин со своими зависимостями публикуется так же — `dotnet publish` кладёт их рядом с плагином.

## Проверка руками

1. Запустить Backend: `dotnet run --project Backend --urls http://127.0.0.1:5116`.
2. Подключить клиент к хабу `/hubs/ui` и взять его `ConnectionId` (клиент из H4 или любой SignalR-клиент
   с обработчиками `ShowInputBox` и `ShowMessage`).
3. `GET /commands` вернёт `sample.echo.ask`.
4. `POST /commands/sample.echo.ask/execute` с заголовком `X-UI-Session: <ConnectionId>` и телом
   `{"connectionId": null, "currentPath": "/home", "selectedPaths": ["/home/a.txt"]}` вернёт `202 {"runId": ...}`,
   а клиент получит `ShowInputBox`, затем `ShowMessage` с введённым текстом и выбранными путями.
