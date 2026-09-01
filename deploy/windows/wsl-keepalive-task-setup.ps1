# One-time setup: registers a Scheduled Task that keeps a permanent WSL2
# client attached in the background ("wsl.exe -- sleep infinity"), so WSL2
# never sees "0 attached clients" and never auto-terminates the VM - this
# is what actually keeps bettingai.service reachable after every console is
# closed. vmIdleTimeout=-1 in .wslconfig alone was NOT enough on this
# machine (confirmed: the VM still stopped within seconds of the last
# client detaching, despite that setting) - a permanently-attached client
# sidesteps the whole idle-shutdown mechanism instead of depending on it.
#
# Run this ONCE from an elevated PowerShell. The task itself needs no
# further attention afterwards - Windows starts it at boot and keeps it
# running (with a restart-on-failure policy) with no visible window.

$ErrorActionPreference = 'Stop'

$action = New-ScheduledTaskAction -Execute 'wsl.exe' -Argument '-- sleep infinity'

$trigger = New-ScheduledTaskTrigger -AtStartup

$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

# ExecutionTimeLimit defaults to 3 days for scheduled tasks - without
# overriding it to zero (= unlimited), Windows would silently kill this
# "forever" process after 3 days and the VM would start idling out again.
# RestartCount/RestartInterval bring it back automatically if it ever does
# get killed (e.g. a Windows update interrupting it).
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -DontStopOnIdleEnd -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName "BettingAI-WSLKeepAlive" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Garde un client WSL2 attaché en permanence pour empêcher l'auto-shutdown du VM (nécessaire pour bettingai.service)"

# Start it now too, no need to wait for the next reboot.
Start-ScheduledTask -TaskName "BettingAI-WSLKeepAlive"

Write-Host "Tâche enregistrée et démarrée. Vérifie avec: wsl -l -v (doit rester 'Running')"
