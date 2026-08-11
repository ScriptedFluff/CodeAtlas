<#
.SYNOPSIS
    Uninstall User-Level CodeGraph Relay Service
.DESCRIPTION
    Stops running relay daemon PowerShell processes and removes scheduled tasks / startup shortcuts.
#>

$TaskName = "CodeGraphRelayDaemon"

Write-Host "Stopping running Relay Daemon processes..." -ForegroundColor Cyan
Get-WmiObject Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains("RelayService.ps1") } | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Host "Removing Scheduled Task if present..." -ForegroundColor Cyan
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

$StartupFolder = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup")
$ShortcutPath = Join-Path $StartupFolder "CodeGraphRelay.lnk"
if (Test-Path $ShortcutPath) {
    Remove-Item $ShortcutPath -Force -ErrorAction SilentlyContinue
    Write-Host "Removed Startup shortcut." -ForegroundColor Green
}

Write-Host "CodeGraph Relay Daemon uninstalled." -ForegroundColor Green
