# Установка клиента клуба (служба-страж + оболочка) на ПК. Запускать от администратора.
# Где взять файлы: распакуйте ClubShell.Client-win-x64.zip из релиза GitHub (там же лежит этот скрипт)
# или соберите сами (.\scripts\build.ps1 → папка dist).
#   .\install.ps1 -PcToken "токен из админки" [-PcName PC-05] [-Zone ARENA] [-ServerUrl https://cryvx.ru]
#                         — установить и сразу подключить ПК к серверу клуба
#   .\install.ps1 -DefenderExclusion — дополнительно добавить папку программы в исключения Защитника Windows
#                         (пока exe не подписаны сертификатом, Защитник иногда принимает хук клавиатуры за угрозу)
#   .\install.ps1 -Uninstall — удалить службу (файлы остаются)
# Раскладка: C:\Program Files\ClubShell\versions\<версия>\ — дальше служба обновляет себя сама (UpdateWorker).
param(
    [switch]$Uninstall,
    [string]$ServerUrl = "https://cryvx.ru",
    [string]$PcToken = "",
    [string]$PcName = "",
    [string]$Zone = "",
    [switch]$DefenderExclusion
)
$ErrorActionPreference = "Stop"
$name = "ClubShellService"
$root = "C:\Program Files\ClubShell"

if ($Uninstall) {
    sc.exe stop $name   | Out-Null
    sc.exe delete $name | Out-Null
    Write-Host "Служба удалена. Файлы в $root оставлены."
    return
}

# Откуда ставить: рядом со скриптом (распакованный пакет клиента) или ..\dist (своя сборка).
$src = if (Test-Path (Join-Path $PSScriptRoot "ClubShell.Service.exe")) { $PSScriptRoot } else { Join-Path (Split-Path $PSScriptRoot -Parent) "dist" }
if (-not (Test-Path (Join-Path $src "ClubShell.Service.exe"))) { throw "Не найден ClubShell.Service.exe: распакуйте пакет клиента или выполните .\scripts\build.ps1" }
if (-not (Test-Path (Join-Path $src "locker\ClubShell.Locker.exe"))) { throw "В пакете нет locker\ClubShell.Locker.exe" }

$ver = (Get-Item (Join-Path $src "ClubShell.Service.exe")).VersionInfo.ProductVersion
if (-not $ver) { $ver = "1.0.0" }
$ver = ($ver -split '\+')[0]
$target = Join-Path $root "versions\$ver"

sc.exe stop $name 2>$null | Out-Null
Start-Sleep -Seconds 2
Get-Process ClubShell.Locker -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$src\*" $target -Recurse -Force

# Права: менять файлы программы могут только SYSTEM и администраторы, обычные пользователи — только читать/запускать.
icacls $root /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" "Users:(OI)(CI)RX" | Out-Null

# Подключение к серверу клуба: дописываем адрес и токен в config.json (остальные настройки и PIN сохраняются).
if ($PcToken) {
    $cfgDir  = "C:\ProgramData\ClubShell"
    $cfgFile = Join-Path $cfgDir "config.json"
    New-Item -ItemType Directory -Force $cfgDir | Out-Null
    $cfg = if (Test-Path $cfgFile) { Get-Content $cfgFile -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
    $set = { param($k, $v) if ($cfg.PSObject.Properties[$k]) { $cfg.$k = $v } else { $cfg | Add-Member -NotePropertyName $k -NotePropertyValue $v } }
    & $set "ServerUrl" $ServerUrl.TrimEnd("/")
    & $set "PcToken" $PcToken.Trim()
    if ($PcName) { & $set "PcName" $PcName }
    if ($Zone)   { & $set "Zone" $Zone }
    [IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    # токен ПК читают только SYSTEM и администраторы
    icacls $cfgDir /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" | Out-Null
    Write-Host "ПК подключён к $ServerUrl."
}

if ($DefenderExclusion) {
    try { Add-MpPreference -ExclusionPath $root; Write-Host "Папка $root добавлена в исключения Защитника." }
    catch { Write-Warning "Не удалось добавить исключение Защитника: $($_.Exception.Message)" }
}

$exe = Join-Path $target "ClubShell.Service.exe"
if (Get-Service $name -ErrorAction SilentlyContinue) {
    sc.exe config $name binPath= "`"$exe`"" start= auto | Out-Null
} else {
    sc.exe create $name binPath= "`"$exe`"" start= auto DisplayName= "ClubShell Service" | Out-Null
}
# Если службу убили, она упала или вышла с кодом ошибки (так она перезапускается после обновления) — поднять через секунду.
sc.exe failure $name reset= 0 actions= restart/1000/restart/1000/restart/1000 | Out-Null
sc.exe failureflag $name 1 | Out-Null
sc.exe start $name | Out-Null

Start-Sleep -Seconds 3
$pinFile = "C:\ProgramData\ClubShell\admin-pin.txt"
Write-Host "Установлена версия $ver в $target."
if (Test-Path $pinFile) { Write-Host "PIN администратора: $pinFile (прочитайте и удалите файл)." }
