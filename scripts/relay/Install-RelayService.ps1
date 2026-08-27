<#
.SYNOPSIS
    Install User-Level CodeAtlas Relay Service
.DESCRIPTION
    Registers a Windows Task Scheduler task or Startup folder VBScript shortcut (RunHidden.vbs)
    running under non-admin user privileges to automatically start the Relay Daemon at user logon.
#>

param (
    [int]$Port = 9123
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DaemonScript = Join-Path $ScriptDir "RelayService.ps1"
$VbsScript = Join-Path $ScriptDir "RunHidden.vbs"

if (-not (Test-Path $DaemonScript)) {
    Write-Error "Could not find RelayService.ps1 at $DaemonScript"
    exit 1
}

$TaskName = "CodeAtlasRelayDaemon"
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Action = New-ScheduledTaskAction -Execute "wscript.exe" -Argument "`"$VbsScript`""
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0

Write-Host "Registering User Scheduled Task: $TaskName..." -ForegroundColor Cyan

try {
    Register-ScheduledTask -TaskName $TaskName -Trigger $Trigger -Action $Action -Settings $Settings -Force | Out-Null
    Write-Host "Successfully registered scheduled task '$TaskName' to start on user logon." -ForegroundColor Green
}
catch {
    Write-Host "Scheduled task registration encountered an issue: $_. Falling back to Startup folder VBScript shortcut..." -ForegroundColor Yellow

    $StartupFolder = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup")
    $ShortcutPath = Join-Path $StartupFolder "CodeAtlasRelay.lnk"
    
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = "wscript.exe"
    $Shortcut.Arguments = "`"$VbsScript`""
    $Shortcut.WindowStyle = 7 # Minimized/Hidden
    $Shortcut.Save()
    
    Write-Host "Created zero-flash Startup shortcut at: $ShortcutPath" -ForegroundColor Green
}

# Start process now via VBScript wrapper
Write-Host "Starting daemon process in background via RunHidden.vbs..." -ForegroundColor Cyan
Start-Process "wscript.exe" -ArgumentList "`"$VbsScript`""

Write-Host "Checking service health status on http://localhost:$Port/health..." -ForegroundColor Cyan
Start-Sleep -Seconds 2

try {
    $health = Invoke-RestMethod -Uri "http://localhost:$Port/health" -TimeoutSec 3
    Write-Host "Relay Daemon operational! Response: $($health | ConvertTo-Json -Compress)" -ForegroundColor Green
}
catch {
    Write-Host "Warning: Could not connect to http://localhost:$Port/health yet. Check log at %LOCALAPPDATA%\CodeAtlasRelay\relay.log" -ForegroundColor Yellow
}
