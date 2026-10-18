# Сборка всего в папку dist\ (запускать на Windows из корня проекта).
#   .\scripts\build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root "dist"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

Push-Location (Join-Path $root "ui")
npm ci
npm run build                       # даёт ui\dist, который подхватит Locker
Pop-Location

dotnet publish (Join-Path $root "src\ClubShell.Locker")  -c Release -r win-x64 --self-contained false -o (Join-Path $out "locker")
dotnet publish (Join-Path $root "src\ClubShell.Service") -c Release -r win-x64 --self-contained false -o $out
dotnet publish (Join-Path $root "src\ClubShell.Admin")   -c Release -r win-x64 --self-contained false -o (Join-Path $out "admin")   # админ-панель: свой exe, ставится на компьютер администратора

Write-Host "Готово: $out"
