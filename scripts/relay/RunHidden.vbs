Dim WinScriptHost
Set WinScriptHost = CreateObject("WScript.Shell")
Dim scriptDir
scriptDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
Dim daemonScript
daemonScript = scriptDir & "\RelayService.ps1"
WinScriptHost.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & daemonScript & """", 0, False
Set WinScriptHost = Nothing
