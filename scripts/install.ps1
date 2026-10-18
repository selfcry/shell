# Установка службы-стража. Запускать от администратора ПОСЛЕ build.ps1.
#   .\scripts\install.ps1            — установить и запустить
#   .\scripts\install.ps1 -Uninstall — удалить
param([switch]$Uninstall)
$ErrorActionPreference = "Stop"
$name   = "ClubShellService"
$target = "C:\Program Files\ClubShell"
$src    = Join-Path (Split-Path $PSScriptRoot -Parent) "dist"

if ($Uninstall) {
    sc.exe stop $name   | Out-Null
    sc.exe delete $name | Out-Null
    Write-Host "Служба удалена. Файлы в $target оставлены."
    return
}

if (-not (Test-Path $src)) { throw "Нет папки dist. Сначала .\scripts\build.ps1" }
sc.exe stop $name 2>$null | Out-Null
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$src\*" $target -Recurse -Force

# Права: менять файлы программы могут только SYSTEM и администраторы, обычные пользователи — только читать/запускать.
icacls $target /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" "Users:(OI)(CI)RX" | Out-Null

sc.exe create $name binPath= "`"$target\ClubShell.Service.exe`"" start= auto DisplayName= "ClubShell Service" | Out-Null
# Если службу убили или она упала — перезапустить через 1 секунду, всегда.
sc.exe failure $name reset= 0 actions= restart/1000/restart/1000/restart/1000 | Out-Null
sc.exe start $name | Out-Null

Start-Sleep -Seconds 3
$pinFile = "C:\ProgramData\ClubShell\admin-pin.txt"
Write-Host "Установлено."
if (Test-Path $pinFile) { Write-Host "PIN администратора: $pinFile (прочитайте и удалите файл)." }
