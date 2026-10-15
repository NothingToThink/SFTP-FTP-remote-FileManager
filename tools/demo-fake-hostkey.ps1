# Демо «подмена ключа»: записывает заведомо неверный SHA-256 отпечаток для host:port в known_hosts.json.
# Использование: pwsh tools/demo-fake-hostkey.ps1 <host> <port> [-Restore]
# Запускать при ЗАКРЫТОМ SWINE: сервер читает файл при старте и перезаписывает его целиком.
param(
    [Parameter(Mandatory, Position = 0)][string]$HostName,
    [Parameter(Mandatory, Position = 1)][int]$Port,
    [switch]$Restore
)
$ErrorActionPreference = "Stop"

$base = if ($env:REMOTE_FILE_MANAGER_DATA_DIR) { $env:REMOTE_FILE_MANAGER_DATA_DIR }
        else { [Environment]::GetFolderPath("ApplicationData") }
$file = Join-Path (Join-Path $base "RemoteFileManager") "known_hosts.json"

$key = "$($HostName.Trim().ToLowerInvariant()):$Port"
$entries = @()
if (Test-Path $file) { $entries = @(Get-Content $file -Raw | ConvertFrom-Json) }
$kept = @($entries | Where-Object { "$($_.Host.Trim().ToLowerInvariant()):$($_.Port)" -ne $key })

if ($Restore) {
    Write-Host "Удалено записей: $($entries.Count - $kept.Count) ($key) из $file"
} else {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $fake = [Convert]::ToBase64String($bytes).TrimEnd("=")
    $kept += [pscustomobject]@{
        Host = $HostName; Port = $Port; Algorithm = "ssh-ed25519"
        Sha256Fingerprint = $fake; TrustedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    Write-Host "Записан неверный отпечаток SHA256:$fake для $key в $file"
}

New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
$json = if ($kept.Count -eq 0) { "[]" } else { ConvertTo-Json -InputObject $kept -Depth 4 }
Set-Content -Path $file -Value $json -Encoding UTF8
