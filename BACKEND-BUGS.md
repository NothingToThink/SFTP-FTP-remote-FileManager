# Баги Backend/Core, найденные headless-аудитом клиента (ветка dev @ 95bd42b)

Править бэк со стороны клиента нельзя, поэтому оба пункта задокументированы для передачи.
Оба воспроизводятся детерминированно; репро шаги проверялись curl'ом против живого сервера.

## BUG-1 (блокер для Local-протокола): листинг отваливается после смены директории — путь удваивается

**Симптом.** Для профиля `Protocol = Local` любой заход в подпапку делает `GET /connections/{id}/filesystem`
неработающим до перезапуска соединения:

```
POST  /connections/{id}/filesystem/dir/current   тело "audit-dir-many"   -> 200 (ок)
GET   /connections/{id}/filesystem/dir/current                            -> "audit-dir-many" (ок)
GET   /connections/{id}/filesystem                                        -> 500
```

**Исключение на сервере:**

```
System.IO.DirectoryNotFoundException:
  Could not find a part of the path '...\Backend\tmp\audit-dir-many\audit-dir-many'.
  at Core.Implementations.Protocol.LocalConnection.GetFilesAsync(...) 
  at Backend.Controllers.ConnectionsFilesystemController.GetAllFiles(...)
```

**Корневая причина.** `ConnectionsFilesystemController.GetAllFiles` передаёт рабочую директорию
в `GetFilesAsync`:

```csharp
var workingDir = await connection.GetWorkingDirectoryAsync(ct); // Local: относительный путь "audit-dir-many"
return Ok(await connection.GetFilesAsync(workingDir, ct));
```

а `LocalConnection.GetLocalPath` приписывает к относительному пути ещё и текущую папку:

```csharp
combined = Path.Combine(_rootPath, _currentPath, remotePath);
// root + "audit-dir-many" + "audit-dir-many"  ->  удвоение
```

В корне (`_currentPath == ""`) не проявляется, поэтому smoke-тесты по корню проходят.
SFTP/FTP не затронуты: у них `GetWorkingDirectoryAsync` возвращает абсолютный путь.

**Варианты фикса (на выбор бэкенда):**
1. `LocalConnection.GetWorkingDirectoryAsync` отдавать абсолютный путь от корня песочницы
   (`_currentPath == "" ? "/" : "/" + _currentPath.Replace('\\','/')`) — семантика сравняется
   с SFTP (`GetLocalPath` уже корректно обрабатывает ведущий `/`), а `GetRelativePath`
   в `ChangeDirectoryAsync` продолжит хранить относительный `_currentPath`.
2. Либо в `GetAllFiles` не передавать путь: `GetFilesAsync("", ct)` — но нужно проверить,
   что SFTP-клиент принимает пустой путь (Renci `ListDirectory("")`).

**Клиентский костыль (уже сделан в UI, чтобы не рассинхронизироваться с сервером):** при ошибке
листинга после смены папки клиент возвращает рабочую директорию сервера назад (`PATCH dir/current`
на предыдущий путь). До фикса бэка в Local-профилях можно работать только с корнем песочницы.

## BUG-2 (некритично): ArgumentException отдаётся как 500 вместо 400

**Симптом.** Запросы к несуществующему пути возвращают 500:

```
PATCH /connections/{id}/filesystem/dir/current  тело "no-such-dir"  -> 500 {"error":"Internal server error"}
```

**Причина.** `LocalConnection` сигнализирует «не существует» через `ArgumentException`
(`throw new ArgumentException($"directory {path} not exists")`), а `ExceptionMiddleware`
мапит в коды только `KeyNotFoundException -> 404` и `InvalidOperationException -> 400`;
`ArgumentException` падает в общий catch -> 500. В логах при этом «Unexpected error».

**Предложение.** Добавить в `ExceptionMiddleware` ветку `catch (ArgumentException) -> 400`
(текст ошибки уже человекочитаемый). Клиенту не блокирует — он читает `{"error": ...}` при любом коде,
но 500 маскирует реальную причину в логах и мониторинге.


## BUG-3 (регрессия в dev): контроллер потерял хендлеры dir/size, dir/copy, dir/move

**Симптом.** После мержей #36/#39 в актуальном dev (`95bd42b`) запросы отдают 404:

```
GET   /connections/{id}/filesystem/dir/size   -> 404
POST  /connections/{id}/filesystem/dir/copy   -> 404
PATCH /connections/{id}/filesystem/dir/move   -> 404
```

При этом интерфейс `Core.Interfaces.Protocol.Connection` по-прежнему объявляет
`GetDirSizeAsync`/`CopyDirAsync`/`MoveDirAsync` (появились в #29), а
`ConnectionsFilesystemController` эти три хендлера больше не содержит.
Похоже, при мерже #36 (endpoint documentation) или #39 (tests+CI) версия контроллера
откатилась до состояния до #29 и съела новые эндпоинты.

**Влияние на клиент.** Копирование/перемещение папок через UI отдают ошибку;
«Свойства» папки показывают размер как «неизвестен» (graceful). Локально в клиенте
шаги smoke по этим эндпоинтам помечаются [SKIP], чтобы регрессия бэка не маскировалась
под полом клиента.

**Виновник найден точно.** Коммит `6387f69` «endpoint documenation (#36)»
(Tafi7, 03.10.2026) переписывал контроллеры под документацию и взял за основу
версию **до** #29 — три хендлера выпали при bulk-rewrite (дифф #36 показывает их
удаление минусами; сам #29 добавил их коммитом `e7d91f1`).

**Живые копии кода** (хендлеры не менялись с #29, можно брать как есть):
- ветка `origin/extending-connection-functionality` (исходный #29) — эталон;
- также есть в `origin/dev-ff`, `origin/feature/backend-integration-tests`, `origin/feature/tests-ci`.

Возврат — ~40 строк: `GET dir/size`, `POST dir/copy`, `PATCH dir/move`
(diff контроллера `origin/dev..origin/extending-connection-functionality`, +174 строки
включая XML-доки; сами хендлеры — копипаст из #29).

## Нюанс контракта (не баг, зафиксировать в документации API)

`Ok(string)` в ASP.NET Core отдаётся **без JSON-обёртки** (StringOutputFormatter, text/plain,
без кавычек): `GET filesystem/dir/current` возвращает сырую строку `audit-dir-many`, а не `"audit-dir-many"`.
Клиент это уже терпит (парсит и сырой текст, и JSON). Остальные эндпоинты (`Ok(Guid)`, `Ok(bool)`,
`Ok(List<...>)`) отдают нормальный JSON.
