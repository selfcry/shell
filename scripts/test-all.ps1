# Проверка «всё вместе» на Windows-виртуалке: сервер + оболочка (рабочий стол гостя) + админ-панель.
#   .\scripts\test-all.ps1                 скачать последние сборки из GitHub Releases и запустить
#   .\scripts\test-all.ps1 -LocalZips D:\z папка с уже скачанными zip (если у ВМ нет доступа к GitHub)
#   .\scripts\test-all.ps1 -Stop           остановить всё запущенное
# Служба-страж здесь НЕ ставится и ничего не блокируется: оболочка работает в тестовом режиме (выход: Ctrl+Shift+F12, PIN 0000).
param(
    [string]$Repo = "selfcry/shell",
    [string]$Tag = "latest",
    [string]$Dir = (Join-Path $env:USERPROFILE "ClubShellTest"),
    [string]$LocalZips = "",
    [int]$Port = 5080,
    [switch]$KeepData,
    [switch]$AllowPower,   # кнопки «Перезагрузить» и «Выключить» в админке реально перезагружают/выключают этот ПК (только для виртуалки!)
    [switch]$ServerOnly,   # только сервер и автопроверки API (без окон оболочки и админки); так скрипт гоняется в CI
    [switch]$Stop
)
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Stop-All {
    foreach ($n in "ClubShell.Server", "ClubShell.Locker", "ClubShell.Admin") {
        Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    # Если оболочку закрыли силой, панель задач Windows и клавиша Win могли остаться спрятанными/сломанными: возвращаем.
    $lk = Join-Path $Dir "locker\ClubShell.Locker.exe"
    if (Test-Path $lk) { Start-Process -FilePath $lk -ArgumentList "--restore-taskbar" -Wait }
}

if ($Stop) { Stop-All; Write-Host "Остановлено."; return }

$script:fails = 0
function Check([string]$name, [bool]$ok, [string]$extra = "") {
    if ($ok) { Write-Host ("  OK    " + $name) -ForegroundColor Green }
    else { Write-Host ("  ОШИБКА " + $name + " " + $extra) -ForegroundColor Red; $script:fails++ }
}

$base = "http://127.0.0.1:$Port"

# ---------- 1. скачать и распаковать ----------
Write-Host "1/6 Подготовка сборок" -ForegroundColor Cyan
Stop-All
Start-Sleep -Seconds 1
New-Item -ItemType Directory -Force $Dir | Out-Null
$parts = @(@{ Zip = "ClubShell.Server-win-x64.zip"; Folder = "server" })
if (-not $ServerOnly) {
    $parts += @{ Zip = "ClubShell.Locker-win-x64.zip"; Folder = "locker" }
    $parts += @{ Zip = "ClubShell.Admin-win-x64.zip";  Folder = "admin" }
}
foreach ($p in $parts) {
    $zip = Join-Path $Dir $p.Zip
    if ($LocalZips) {
        Copy-Item (Join-Path $LocalZips $p.Zip) $zip -Force
    } else {
        $url = "https://github.com/$Repo/releases/download/$Tag/$($p.Zip)"
        Write-Host "  скачиваю $url"
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    }
    $target = Join-Path $Dir $p.Folder
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $target -Force
    Get-ChildItem $target -Recurse -File | Unblock-File -ErrorAction SilentlyContinue   # убрать метку «скачано из интернета»
}

# ---------- 2. сервер ----------
Write-Host "2/6 Запуск сервера" -ForegroundColor Cyan
$data = Join-Path $Dir "data"
if (-not $KeepData -and (Test-Path $data)) { Remove-Item $data -Recurse -Force }
$env:Club__DataDir = $data
$env:ASPNETCORE_URLS = $base
$env:Club__SeedDemo = "true"
$serverExe = Join-Path $Dir "server\ClubShell.Server.exe"
$serverLog = Join-Path $Dir "server.log"
Start-Process -FilePath $serverExe -WorkingDirectory (Split-Path $serverExe) -WindowStyle Hidden `
    -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $Dir "server.err.log") | Out-Null

$up = $false
for ($i = 0; $i -lt 40; $i++) {
    try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; $up = $true; break } catch { Start-Sleep -Milliseconds 500 }
}
Check "сервер отвечает на $base/health" $up "(лог: $serverLog)"
if (-not $up) { throw "Сервер не запустился. Смотрите $serverLog и server.err.log" }

$key = (Get-Content (Join-Path $data "admin-key.txt") -Raw).Trim()
$adm = @{ "X-Admin-Key" = $key }

function Call($method, $path, $headers, $body = $null) {
    try {
        $args2 = @{ Uri = "$base$path"; Method = $method; Headers = $headers; ContentType = "application/json"; TimeoutSec = 10 }
        if ($null -ne $body) { $args2.Body = ($body | ConvertTo-Json -Compress) }
        return @{ Status = 200; Data = (Invoke-RestMethod @args2) }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return @{ Status = $code; Data = $null }
    }
}

# ---------- 3. ПК и автопроверки API ----------
Write-Host "3/6 Автопроверки сервера" -ForegroundColor Cyan
Check "админ-API без ключа закрыт (401)" ((Call GET "/api/admin/stats" @{}).Status -eq 401)

# ПК с таким именем могло остаться от прошлого запуска (-KeepData): удаляем и создаём заново, токен показывается только при создании
$pcName = "VM-PC-01"
$old = (Call GET "/api/admin/pcs" $adm).Data | Where-Object { $_.name -eq $pcName }
if ($old) { Call DELETE "/api/admin/pcs/$($old.id)" $adm | Out-Null }
$newPc = Call POST "/api/admin/pcs" $adm @{ name = $pcName; zone = "ARENA" }
Check "ПК $pcName создан, получен токен" ($newPc.Status -eq 200 -and $newPc.Data.token)
$pcToken = $newPc.Data.token
$pc = @{ "X-Pc-Token" = $pcToken }

Check "ПК с чужим токеном не пускают (401)" ((Call POST "/api/pc/login" @{ "X-Pc-Token" = "bad" } @{ username = "demo"; password = "demo" }).Status -eq 401)
$bad = Call POST "/api/pc/login" $pc @{ username = "demo"; password = "wrong" }
Check "неверный пароль отклонён" ($bad.Status -eq 200 -and $bad.Data.ok -eq $false)
$good = Call POST "/api/pc/login" $pc @{ username = "demo"; password = "demo" }
Check "вход demo/demo, есть тарифы" ($good.Data.ok -eq $true -and $good.Data.packages.Count -ge 3)

# отдельный гость для проверки сеанса, чтобы не тратить баланс demo
$chk = "check" + (Get-Random -Minimum 1000 -Maximum 9999)
Check "администратор создаёт гостя $chk с балансом 500" ((Call POST "/api/admin/guests" $adm @{ login = $chk; password = "check12345"; balance = 500 }).Status -eq 200)
$reg = "reg" + (Get-Random -Minimum 1000 -Maximum 9999)
$r = Call POST "/api/pc/register" $pc @{ username = $reg; password = "secret123"; birth = "2000-01-01" }
Check "самостоятельная регистрация гостя $reg" ($r.Data.ok -eq $true -and $r.Data.balance -eq 0)
$dup = Call POST "/api/pc/register" $pc @{ username = $reg; password = "secret123"; birth = "2000-01-01" }
Check "повторный логин занят" ($dup.Data.ok -eq $false)

$steal = Call POST "/api/pc/session/start" $pc @{ username = $chk; packageId = "h1" }
Check "без входа гостя сеанс за его счёт не купить" ($steal.Data.ok -eq $false)
Check "вход $chk" ((Call POST "/api/pc/login" $pc @{ username = $chk; password = "check12345" }).Data.ok -eq $true)
$st = Call POST "/api/pc/session/start" $pc @{ username = $chk; packageId = "h1" }
Check "старт сеанса: списано 140, баланс 360" ($st.Data.ok -eq $true -and $st.Data.balance -eq 360 -and $st.Data.minutes -eq 60)
$st2 = Call POST "/api/pc/session/start" $pc @{ username = $chk; packageId = "h1" }
Check "второй сеанс на том же ПК запрещён" ($st2.Data.ok -eq $false)
$hb = Call POST "/api/pc/heartbeat" $pc @{ username = $chk }
Check "пульс: сервер видит сеанс и остаток" ($hb.Data.ok -eq $true -and $hb.Data.session.remainingSec -gt 3500)
$sess = (Call GET "/api/admin/sessions?active=true" $adm).Data
Check "админка видит активный сеанс" (($sess | Where-Object { $_.guest -eq $chk }) -ne $null)
$sid = ($sess | Where-Object { $_.guest -eq $chk }).id
Check "админ добавляет 15 минут" ((Call POST "/api/admin/sessions/$sid/add-time" $adm @{ minutes = 15 }).Data.ok -eq $true)
Call POST "/api/pc/session/stop" $pc @{ username = $chk } | Out-Null
Check "сеанс завершён" (((Call GET "/api/admin/sessions?active=true" $adm).Data | Where-Object { $_.guest -eq $chk }) -eq $null)
Check "сводка админки отвечает" ((Call GET "/api/admin/stats" $adm).Data.guests -ge 4)

# касса, бонусы, промокод, вызов, бронь
$cur = (Call GET "/api/admin/shift" $adm).Data
if ($cur.shift) { Call POST "/api/admin/shift/close" $adm @{ cash = 0 } | Out-Null }
Check "смена открыта" ((Call POST "/api/admin/shift/open" $adm @{ cashier = "Тест"; cash = 1000 }).Data.ok -eq $true)
$chkId = ((Call GET "/api/admin/guests?q=$chk" $adm).Data | Where-Object { $_.login -eq $chk }).id
Check "пополнение наличными в смену" ((Call POST "/api/admin/guests/$chkId/topup" $adm @{ amount = 300; method = "cash" }).Data.ok -eq $true)
$rep = (Call GET "/api/admin/shift" $adm).Data.report
Check "отчёт смены: наличные 300, в кассе 1300" ($rep.topupCash -eq 300 -and $rep.cashExpected -eq 1300)
$promo = "T" + (Get-Random -Minimum 10000 -Maximum 99999)
Call PUT "/api/admin/promos" $adm @{ code = $promo; bonus = 50; maxUses = 5 } | Out-Null
Call POST "/api/pc/login" $pc @{ username = $chk; password = "check12345" } | Out-Null
Check "промокод даёт бонусы" ((Call POST "/api/pc/promo" $pc @{ username = $chk; code = $promo }).Data.bonus -ge 50)
Check "повторно промокод не принимается" ((Call POST "/api/pc/promo" $pc @{ username = $chk; code = $promo }).Data.ok -eq $false)
Check "вызов администратора виден в админке" ((Call POST "/api/pc/call" $pc @{ username = $chk }).Data.ok -eq $true -and ((Call GET "/api/admin/calls" $adm).Data | Where-Object { $_.pc -eq $pcName }))
$pcId = ((Call GET "/api/admin/pcs" $adm).Data | Where-Object { $_.name -eq $pcName }).id
Call POST "/api/admin/calls/$pcId/ack" $adm | Out-Null
$soon = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 300
Check "бронь создана" ((Call POST "/api/admin/bookings" $adm @{ pcId = $pcId; login = "demo"; starts = $soon; minutes = 60 }).Data.ok -eq $true)
Check "чужой гость не входит на забронированный ПК" ((Call POST "/api/pc/login" $pc @{ username = $chk; password = "check12345" }).Data.ok -eq $false)
$bk = ((Call GET "/api/admin/bookings" $adm).Data | Where-Object { $_.pcId -eq $pcId }).id
Call DELETE "/api/admin/bookings/$bk" $adm | Out-Null
Check "смена закрыта, отчёт пришёл" ((Call POST "/api/admin/shift/close" $adm @{ cash = 1300 }).Data.report.cashDiff -eq 0)
Check "аналитика отвечает" ((Call GET "/api/admin/analytics?days=7" $adm).Status -eq 200)

Write-Host ""
if ($script:fails -eq 0) { Write-Host "Автопроверки сервера: все пройдены." -ForegroundColor Green }
else { Write-Host "Автопроверки сервера: ошибок $($script:fails). Дальше смотрите server.log." -ForegroundColor Red }

if ($ServerOnly) {
    Stop-All
    if ($script:fails -gt 0) { exit 1 }
    return
}

# ---------- 4. оболочка ----------
Write-Host "4/6 Запуск оболочки (тестовый режим, подключена к серверу)" -ForegroundColor Cyan
$env:CLUBSHELL_TEST = "1"
$env:CLUBSHELL_SERVER_URL = $base
$env:CLUBSHELL_PC_TOKEN = $pcToken
if ($AllowPower) { $env:CLUBSHELL_TEST_POWER = "1" }
Start-Process -FilePath (Join-Path $Dir "locker\ClubShell.Locker.exe") -WorkingDirectory (Join-Path $Dir "locker")

# ---------- 5. админка ----------
Write-Host "5/6 Запуск админ-панели" -ForegroundColor Cyan
Start-Process -FilePath (Join-Path $Dir "admin\ClubShell.Admin.exe") -WorkingDirectory (Join-Path $Dir "admin")
try { Set-Clipboard -Value $key; $clip = "ключ уже в буфере обмена (Ctrl+V)" } catch { $clip = "ключ: $key" }

Write-Host "6/6 Дальше проверяйте руками" -ForegroundColor Cyan
Write-Host @"

  АДМИН-ПАНЕЛЬ: адрес сервера  $base
                ключ администратора  - $clip
                (файл: $data\admin-key.txt)

  ОБОЛОЧКА:     пробел -> Вход. Логин demo / пароль demo (баланс 600) или любой гость, созданный в админке.
                Тестовый выход: Ctrl+Shift+F12 на заставке, PIN 0000.

  ЧТО ПРОВЕРИТЬ (подробнее: docs/test-on-windows.md):
    1. В админке на вкладке «Компьютеры» ПК $pcName в сети.
    2. Войти в оболочке, начать сеанс -> в админке ПК стал оранжевым с именем гостя и временем.
    3. В админке: +15 минут -> таймер в оболочке прыгает вперёд; «Сообщение» -> тост в оболочке.
    4. В админке «Завершить сеанс» -> оболочка возвращается к блокировке (перезагрузки в тесте нет).
    5. Регистрация нового гостя в оболочке -> он виден во вкладке «Гости».
    6. Пополнить баланс гостя в админке -> новый баланс виден в оболочке при следующем входе.

  Остановить всё: .\scripts\test-all.ps1 -Stop
  Логи сервера: $serverLog
"@
if ($script:fails -gt 0) { exit 1 }
