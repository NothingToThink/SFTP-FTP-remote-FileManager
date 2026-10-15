# Собирает демо-бандл: клиент + сервер, self-contained, в dist/<rid>/ (сервер в dist/<rid>/server/).
# Использование: pwsh tools/publish-demo.ps1 [-Rid win-x64|linux-x64|osx-arm64]
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$out = "dist/$Rid"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "== Backend -> $out/server"
dotnet publish Backend/Backend.csproj -c Release -r $Rid --self-contained true -o "$out/server" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Client -> $out"
dotnet publish Client/FileManagerClient.csproj -c Release -r $Rid --self-contained true -o $out --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Переименовываем apphost: он находит FileManagerClient.dll по встроенному имени, так что имя файла не важно
$exeSuffix = if ($Rid -like "win-*") { ".exe" } else { "" }
Move-Item -Force "$out/FileManagerClient$exeSuffix" "$out/SWINE$exeSuffix"

Write-Host "Готово: $out/SWINE$exeSuffix"
