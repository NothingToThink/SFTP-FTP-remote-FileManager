# FileManagerClient — новый UI для SFTP/FTP-менеджера

Кроссплатформенный (Windows/Linux) десктопный клиент на **Avalonia 12 + C# (.NET 10)**, заменяющий
питоновский PyQt5 GUI из ветки `GUI`. Общается с Backend только по HTTP API, ничего не знает
о внутренностях Core. Стек выбран осознанно: Backend уже на .NET — одна toolchain, общие
DTO-контракты, `dotnet publish` на обе ОС, а запуск дочерних процессов — штатный `System.Diagnostics.Process`.

## Запуск (разработка)

Терминал 1 — Backend (из корня репозитория):

```bash
dotnet run --project Backend --urls http://127.0.0.1:5116
```

Терминал 2 — клиент:

```bash
dotnet run --project Client
```

Адрес сервера настраивается шестерёнкой в статус-баре и сохраняется
в `%APPDATA%/FileManagerClient/settings.json` (на Linux — `~/.config`/`~/.local/share`).

## Изоляция данных для тестов

По умолчанию клиент хранит настройки в `%APPDATA%/FileManagerClient/`, а сервер — профили
и `known_hosts` в `%APPDATA%/RemoteFileManager/`. Для тестов и параллельных запусков обе
папки переопределяются переменными окружения — реальные данные не затираются:

```bash
# сервер: отдельное хранилище профилей/known_hosts
REMOTE_FILE_MANAGER_DATA_DIR=./testdata dotnet run --project Backend --urls http://127.0.0.1:5116

# клиент: отдельные настройки
FILEMANAGERCLIENT_DATA_DIR=./testdata dotnet run --project Client
```

## Проверки

Контрактный smoke-тест без GUI — прогоняет все эндпоинты через Local-протокол,
отчёт в `smoke-result.txt`, код возврата 0 = всё прошло:

```bash
dotnet run --project Client -- --smoke http://127.0.0.1:5116
```

Headless-аудит layout без скриншотов — сценарии (старт/подключение/диалоги/много профилей/
сервер недоступен) на 4 размерах окна, текстовый отчёт о налезаниях/обрезках/выходах за границы:

```bash
dotnet run --project DevAudit -- http://127.0.0.1:5116
# отчёт: DevAudit/bin/Debug/net10.0/audit-out/audit-report.txt
```

## Сборка релиза (клиент + Backend)

Требуется .NET 10 SDK. Оба проекта собираются self-contained под Windows и Linux:

```bash
# Backend как самостоятельный сервер
dotnet publish Backend -c Release -r win-x64 --self-contained /p:PublishSingleFile=true
dotnet publish Backend -c Release -r linux-x64 --self-contained /p:PublishSingleFile=true

# Клиент
dotnet publish Client -c Release -r win-x64 --self-contained
dotnet publish Client -c Release -r linux-x64 --self-contained
```

Результаты: `Backend/bin/Release/net10.0/{rid}/publish/` и `Client/bin/Release/net10.0/{rid}/publish/`.
Дистрибутив — папка с exe клиента + `server/` с опубликованным Backend (см. план бандлинга ниже);
клиент запускается без установленного рантайма. Шрифты Geist зашиты в сборку
(`Client/Assets/Fonts/`, лицензия SIL OFL там же).

Запуск собранного Backend: `Backend.exe --urls http://127.0.0.1:5116` (порт любой свободный).

## Что умеет

- Профили подключений: создание/редактирование/удаление (Local/FTP/SFTP, авторизация
  anonymous/password/key с приватным ключом и passphrase).
- Соединения: подключение/отключение, индикация состояния.
- Файловый браузер: навигация (вверх/по пути/двойной клик), создание файлов и папок,
  переименование, удаление, копирование и перемещение по пути, свойства, сортировка колонок.
- Передачи: upload (multipart) и download (стриминг) файлов; загрузка перетаскиванием
  (drag&drop файлов на файловую панель, мультизагрузка в текущую папку).
- Единая обработка ошибок API (тело `{"error": ...}` из ExceptionMiddleware → диалог + статус-бар).
- Доступность: ключевые элементы размечены `AutomationProperties.AutomationId`
  (кнопки, списки, поля) — для UI-тестов и скринридеров; иконки-кнопки имеют `AutomationProperties.Name`.

## Архитектура

```
Client/
  Api/FileManagerApiClient.cs   — типизированный HTTP-клиент (единственная точка знания о контракте)
  Models/ApiModels.cs           — DTO и JSON-конвертеры, зеркалящие серверные
  Services/ClientSettings.cs    — настройки (URL сервера), %APPDATA%
  Services/IServerLauncher.cs   — шов «кто запускает Backend» (см. ниже)
  Services/DialogService.cs     — модальные диалоги и файловые пикеры
  ViewModels/                   — MVVM (CommunityToolkit.Mvvm), вся логика экранов
  Views/                        — AXAML-разметка
  SmokeTest.cs                  — контрактный self-test (--smoke)
```

MVVM без ссылок на Backend-сборки: замена UI-стека или Backend не затрагивает другой слой.

### Нюансы контракта Backend (dev @ 389c138)

- Ответы — camelCase (дефолт ASP.NET Core), НО `HostProfile` внутри профиля сериализуется
  кастомным конвертером в PascalCase (`Host`/`Protocol`/`Port`/`Auth`) — регистр при отправке важен.
- `AuthData` — полиморфная: `{"$type": "password" | "key" | "anonymous", ...}`.
- Часть GET/DELETE эндпоинтов принимает **путь в теле запроса** как JSON-строку —
  `FileManagerApiClient` это инкапсулирует (`GetWithBodyAsync`/`DeleteWithBodyAsync`).
- Связь «профиль ↔ соединение» на сервере не хранится: трекается на клиенте
  (`MainWindowViewModel._activeConnections`).

## План: сборка и встраивание Backend (следующий этап)

Цель — один дистрибутив, где UI-процесс сам поднимает сервер как **дочерний процесс**:

1. **Сборка Backend как self-contained:**
   ```bash
   dotnet publish Backend -c Release -r win-x64 --self-contained /p:PublishSingleFile=true
   dotnet publish Backend -c Release -r linux-x64 --self-contained /p:PublishSingleFile=true
   ```
2. **Сборка клиента** аналогично; дистрибутив = папка:
   ```
   dist/
     FileManagerClient(.exe)
     server/Backend(.exe)        # опубликованный бэк
   ```
3. **BundledServerLauncher** (реализация `IServerLauncher`, место уже зарезервировано):
   - выбрать свободный порт (`TcpListener(0)` → отпустить);
   - `Process.Start("server/Backend", "--urls http://127.0.0.1:{port}")` с
     `UseShellExecute=false`, `RedirectStandardOutput/Error` для логов в файл;
   - ждать готовность поллингом `GET /connections` до ~15 сек;
   - на выходе приложения — `Process.Kill(entireProcessTree: true)` + graceful через
     `CancellationToken`, в подписке на `IClassicDesktopStyleApplicationLifetime.Exit`;
   - если сервер по адресу уже отвечает — не стартовать второй (реюз существующего).
   Сейчас вместо него работает `ExternalServerLauncher` (сервер запускается снаружи,
   клиент только проверяет доступность) — код ViewModel не меняется при переходе.

## CI/релизы

В репо уже есть GitHub Actions из ветки GUI (`release.yml` для PyInstaller) — при мерже этой ветки
workflow переделывается на `dotnet publish` матрицей `[windows-latest, ubuntu-latest]`
с артефактами `FileManagerClient-{os}-{arch}.zip` (клиент + server/).
