# Ограничения для гостевой учётной записи клуба. Запускать от администратора.
# ВАЖНО: сначала проверьте в виртуальной машине и держите отдельную админ-учётку.
#   .\scripts\harden.ps1 -GuestUser guest              — применить
#   .\scripts\harden.ps1 -GuestUser guest -Undo        — снять
# Работает в Windows 11 Home и Pro: используются настройки профиля пользователя, а не групповые политики.
param(
    [Parameter(Mandatory)] [string]$GuestUser,
    [string]$LockerPath = "C:\Program Files\ClubShell\locker\ClubShell.Locker.exe",
    [switch]$Undo
)
$ErrorActionPreference = "Stop"

$profile = Join-Path "C:\Users" $GuestUser
$hiveFile = Join-Path $profile "NTUSER.DAT"
if (-not (Test-Path $hiveFile)) { throw "Профиль $GuestUser не найден: войдите этим пользователем один раз." }

$tmp = "HKU\ClubGuest"
reg.exe load $tmp $hiveFile | Out-Null
try {
    $base = "Registry::$tmp"
    $sys  = "$base\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    $exp  = "$base\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
    $win  = "$base\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
    $pol  = "$base\Software\Policies\Microsoft\Windows\System"
    $acc  = "$base\Control Panel\Accessibility"

    $values = @(
        @{ Path = $sys; Name = "DisableTaskMgr";        Value = 1 },   # диспетчер задач
        @{ Path = $sys; Name = "DisableLockWorkstation"; Value = 1 },   # Win+L
        @{ Path = $sys; Name = "DisableChangePassword"; Value = 1 },
        @{ Path = $sys; Name = "DisableRegistryTools";  Value = 1 },   # regedit
        @{ Path = $exp; Name = "NoRun";                 Value = 1 },   # Win+R
        @{ Path = $exp; Name = "NoWinKeys";             Value = 1 },   # сочетания с Win
        @{ Path = $exp; Name = "NoControlPanel";        Value = 1 },
        @{ Path = $pol; Name = "DisableCMD";            Value = 1 }    # cmd.exe и .bat даже из диалогов «Открыть» в играх
    )

    # Залипание, фильтрация и озвучивание клавиш (5×Shift, удержание Shift, NumLock 5 c) открывают окна со ссылками
    # в «Параметры» — классический выход из киоска. Флаги без «горячей клавиши»: сами функции работают, вызвать их нельзя.
    $access = @(
        @{ Path = "$acc\StickyKeys"; Value = "506" },
        @{ Path = "$acc\Keyboard Response"; Value = "122" },
        @{ Path = "$acc\ToggleKeys"; Value = "58" }
    )
    foreach ($a in $access) {
        if ($Undo) { continue } # значения по умолчанию Windows вернёт сама при сбросе профиля
        New-Item -Path $a.Path -Force | Out-Null
        New-ItemProperty -Path $a.Path -Name "Flags" -Value $a.Value -PropertyType String -Force | Out-Null
    }

    foreach ($v in $values) {
        if ($Undo) { Remove-ItemProperty -Path $v.Path -Name $v.Name -ErrorAction SilentlyContinue }
        else {
            New-Item -Path $v.Path -Force | Out-Null
            New-ItemProperty -Path $v.Path -Name $v.Name -Value $v.Value -PropertyType DWord -Force | Out-Null
        }
    }

    # Своя оболочка вместо explorer.exe для этого пользователя: нет рабочего стола, панели задач и меню «Пуск».
    if ($Undo) { Remove-ItemProperty -Path $win -Name "Shell" -ErrorAction SilentlyContinue }
    else {
        New-Item -Path $win -Force | Out-Null
        New-ItemProperty -Path $win -Name "Shell" -Value "`"$LockerPath`"" -PropertyType String -Force | Out-Null
    }
}
finally {
    [gc]::Collect(); [gc]::WaitForPendingFinalizers()
    reg.exe unload $tmp | Out-Null
}

Write-Host ($(if ($Undo) { "Ограничения сняты." } else { "Ограничения применены для $GuestUser." }))
Write-Host "Ctrl+Alt+Del ограничивается теми же значениями (диспетчер задач, смена пароля, блокировка); выход из системы оставьте для сброса сессии."
