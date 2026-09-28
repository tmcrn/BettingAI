# Runs as a Task Scheduler job with a wake timer (see the registration
# block at the bottom): wakes the PC at a fixed time, makes sure
# bettingai.service is actually up inside WSL2, triggers one
# /api/auto-decide-bets cycle over HTTP, waits for it to finish, then puts
# the PC back to sleep - "wake, do its thing, sleep again" instead of
# keeping the whole machine awake 24/7 just for one daily cycle.
#
# Must run as the same Windows user that owns the WSL2 "Ubuntu"
# registration (same reasoning as wsl-keepalive-task-setup.ps1 in this
# folder - WSL distro registrations are per-user, a SYSTEM-run task
# wouldn't see it).
#
# NOTE: whether WSL2 itself cleanly pauses/resumes across a real Windows
# sleep (as opposed to just idling out, which wsl-keepalive-task-setup.ps1
# already guards against) hasn't been confirmed on this exact machine -
# the "make sure it's actually running" block below is a safety net for
# that uncertainty either way, but do one manual test run (see the bottom
# of this file) before trusting this unattended overnight.

$ErrorActionPreference = 'Continue' # keep going even if the cycle call fails - still want to sleep the PC back afterward
$port = 5255
$logPath = Join-Path $PSScriptRoot "nightly-cycle.log"

function Write-Log($message) {
    $line = "$(Get-Date -Format o) - $message"
    Write-Host $line
    Add-Content -Path $logPath -Value $line
}

Write-Log "Réveil programmé, démarrage du cycle nocturne"

# Relance WSL2 s'il s'était arrêté pendant qu'on dormait - `-- true` suffit
# à redémarrer le VM sans rien exécuter d'utile à l'intérieur.
wsl.exe -- true

# Attend que bettingai.service soit actif (jusqu'à 60s) - évite d'appeler
# l'API avant que le service ait fini de (re)démarrer.
$serviceReady = $false
for ($i = 0; $i -lt 12; $i++) {
    $status = (wsl.exe -- systemctl is-active bettingai.service 2>$null)
    if ($status -eq 'active') { $serviceReady = $true; break }
    Start-Sleep -Seconds 5
}

if (-not $serviceReady) {
    Write-Log "❌ bettingai.service pas actif après 60s - cycle annulé, remise en veille quand même"
} else {
    try {
        # Timeout généreux - voir AutoDecideBetsEndpoint, un cycle peut
        # prendre jusqu'à une heure dans le pire cas (beaucoup de matchs).
        $response = Invoke-WebRequest -Uri "http://localhost:$port/api/auto-decide-bets" -Method Post -TimeoutSec 3900
        Write-Log "✅ Cycle terminé: $($response.Content)"
    } catch {
        Write-Log "❌ Erreur pendant le cycle: $_"
    }
}

Write-Log "Remise en veille"
Start-Sleep -Seconds 10 # laisse le temps aux derniers logs de s'écrire
rundll32.exe powrprof.dll,SetSuspendState 0,1,0

# --- One-time setup (à faire une seule fois) ---
#
# 1) Autoriser les minuteries de réveil pour le plan d'alimentation actif
#    (sinon Windows ignore silencieusement le "Wake the computer" ci-dessous) :
#
#    powercfg /setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1
#    powercfg /setactive SCHEME_CURRENT
#
#    (Vérifiable/réglable aussi via l'interface : Panneau de configuration >
#    Options d'alimentation > Modifier les paramètres du mode > Modifier
#    les paramètres d'alimentation avancés > Veille > Autoriser les
#    minuteries de réveil > Activé.)
#
# 2) Vérifier dans le BIOS/UEFI que le réveil programmé (RTC wake / wake
#    timer) n'est pas désactivé au niveau matériel - ça varie selon la
#    carte mère, généralement sous un menu "Power Management".
#
# 3) Enregistrer la tâche planifiée (à exécuter UNE FOIS depuis un
#    PowerShell élevé, en étant connecté avec le même utilisateur Windows
#    que celui qui possède la distro WSL "Ubuntu") :
#
#    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File "C:\chemin\vers\nightly-cycle-wake-sleep.ps1"'
#    $trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM
#    $currentUser = "$env:USERDOMAIN\$env:USERNAME"
#    $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
#    $settings = New-ScheduledTaskSettingsSet -WakeToRun -ExecutionTimeLimit (New-TimeSpan -Hours 2) -DontStopOnIdleEnd -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
#    Register-ScheduledTask -TaskName "BettingAI-NightlyCycle" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Réveille le PC à 2h, lance un cycle IA, remet en veille"
#
# 4) TESTE une fois manuellement avant de faire confiance à l'automatique :
#    laisse le PC partir en veille normalement, puis regarde s'il se
#    réveille bien tout seul à l'heure programmée (Planificateur de tâches
#    > historique de la tâche, ou juste vérifier nightly-cycle.log créé à
#    côté de ce script). Si le réveil ne se déclenche pas, c'est presque
#    toujours l'étape 1 (minuteries de réveil) ou 2 (BIOS) qui bloque.
