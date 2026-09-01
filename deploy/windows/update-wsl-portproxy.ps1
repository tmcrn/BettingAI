# Redirects Windows:5255 -> the current WSL2 guest IP for the BettingAI
# dashboard. WSL2's internal IP changes on every VM restart (Windows reboot,
# `wsl --shutdown`, ...), which silently breaks the existing portproxy rule
# (it keeps pointing at the old, now-dead IP) - this re-points it to
# whatever the IP actually is right now, so it's safe to run any time,
# not just once.
#
# Must run elevated (portproxy/firewall changes need admin rights).
# Register as a Scheduled Task ("At log on" or "At startup", run as
# admin) so this happens automatically - see the setup block at the
# bottom of this file for the one-time registration command.

$ErrorActionPreference = 'Stop'

$listenPort = 5255
$wslIp = (wsl hostname -I).Trim().Split(' ')[0]

if ([string]::IsNullOrWhiteSpace($wslIp)) {
    Write-Error "Impossible de récupérer l'IP de WSL2 - le VM est-il démarré ? (essaie 'wsl' une fois pour le lancer, puis relance ce script)"
    exit 1
}

Write-Host "IP WSL2 actuelle: $wslIp"

# Remove any existing rule for this port first - "add" fails if a rule
# already exists, and the old rule may point at a now-stale IP.
netsh interface portproxy delete v4tov4 listenport=$listenPort listenaddress=0.0.0.0 2>$null | Out-Null

netsh interface portproxy add v4tov4 listenport=$listenPort listenaddress=0.0.0.0 connectport=$listenPort connectaddress=$wslIp

Write-Host "Portproxy mis à jour: 0.0.0.0:$listenPort -> ${wslIp}:$listenPort"

# Firewall rule is idempotent to (re)create - New-NetFirewallRule errors if
# a rule with this exact DisplayName already exists, so only add it if missing.
if (-not (Get-NetFirewallRule -DisplayName "BettingAI Dashboard" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "BettingAI Dashboard" -Direction Inbound -Protocol TCP -LocalPort $listenPort -Action Allow | Out-Null
    Write-Host "Règle pare-feu créée."
}

# --- One-time setup: register this script as a Scheduled Task that runs
# automatically at every Windows startup, as admin, no interaction needed.
# Run this block ONCE from an elevated PowerShell (copy-paste just this
# part, not the whole file):
#
# $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\update-wsl-portproxy.ps1"'
# $trigger = New-ScheduledTaskTrigger -AtStartup
# $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest
# Register-ScheduledTask -TaskName "BettingAI-PortProxy" -Action $action -Trigger $trigger -Principal $principal -Description "Redirige Windows:5255 vers l'IP WSL2 courante pour le dashboard BettingAI"
