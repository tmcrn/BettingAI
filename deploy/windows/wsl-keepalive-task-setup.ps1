# One-time setup: registers a Scheduled Task that keeps a permanent WSL2
# client attached in the background ("wsl.exe -- sleep infinity"), so WSL2
# never sees "0 attached clients" and never auto-terminates the VM - this
# is what actually keeps bettingai.service reachable after every console is
# closed. vmIdleTimeout=-1 in .wslconfig alone was NOT enough on this
# machine (confirmed: the VM still stopped within seconds of the last
# client detaching, despite that setting) - a permanently-attached client
# sidesteps the whole idle-shutdown mechanism instead of depending on it.
#
# Run this ONCE from an elevated PowerShell, logged in as the same Windows
# user that owns the "Ubuntu" WSL registration (timothecernon). The task
# itself needs no further attention afterwards - Windows starts it at
# logon and keeps it running (with a restart-on-failure policy) with no
# visible window.
#
# Must run as THIS user, not SYSTEM: WSL distro registrations are
# per-user (under that user's profile), so a task running as SYSTEM
# doesn't see "Ubuntu" at all - confirmed live: with -UserId SYSTEM the
# "sleep infinity" client never actually attached to anything, and the
# VM kept stopping within seconds exactly as before. Trigger is AtLogOn
# (not AtStartup) for the same reason - SYSTEM starts before any user
# profile is loaded, this task needs the user's own session context.

$ErrorActionPreference = 'Stop'

# Remove any previous (broken, SYSTEM-based) registration of this task
# before re-registering - Register-ScheduledTask fails if one already
# exists under this name.
Unregister-ScheduledTask -TaskName "BettingAI-WSLKeepAlive" -Confirm:$false -ErrorAction SilentlyContinue

$action = New-ScheduledTaskAction -Execute 'wsl.exe' -Argument '-- sleep infinity'

$currentUser = "$env:USERDOMAIN\$env:USERNAME"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser

$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited

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
