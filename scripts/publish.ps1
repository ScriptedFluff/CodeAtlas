param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot/..").Path
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $rootDir "publish"))

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Publishing CodeGraphMcp ($Configuration)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Terminate any running server/CLI instances before publishing
Get-Process -Name CodeGraphMcp,CodeGraphMcp.Server,CodeGraphMcp.Cli -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$serverOut = Join-Path $publishDir "server"
$cliOut = Join-Path $publishDir "cli"

# 1. Publish Server & CLI
Write-Host "`n[1/3] Publishing CodeGraphMcp.Server..." -ForegroundColor Green
dotnet publish "$rootDir/src/CodeGraphMcp.Server/CodeGraphMcp.Server.csproj" -c $Configuration -o $serverOut

Write-Host "`n[2/3] Publishing CodeGraphMcp (CLI)..." -ForegroundColor Green
dotnet publish "$rootDir/src/CodeGraphMcp.Cli/CodeGraphMcp.Cli.csproj" -c $Configuration -o $cliOut

# Copy CLI binary to root publish directory and create CodeGraphMcp.Cli.exe alias
Copy-Item -Path "$cliOut\*" -Destination $publishDir -Force
Copy-Item -Path "$cliOut\CodeGraphMcp.exe" -Destination "$cliOut\CodeGraphMcp.Cli.exe" -Force
if (Test-Path "$cliOut\CodeGraphMcp.pdb") {
    Copy-Item -Path "$cliOut\CodeGraphMcp.pdb" -Destination "$cliOut\CodeGraphMcp.Cli.pdb" -Force
}

function Safe-CopyFile {
    param([string]$Source, [string]$Destination)
    $destFile = if (Test-Path $Destination -PathType Container) {
        Join-Path $Destination (Split-Path $Source -Leaf)
    } else {
        $Destination
    }
    try {
        Copy-Item -Path $Source -Destination $destFile -Force -ErrorAction Stop
    } catch {
        if (Test-Path $destFile) {
            $tempBak = "$destFile.$([System.Guid]::NewGuid().ToString('N')).bak"
            try {
                Move-Item -Path $destFile -Destination $tempBak -Force -ErrorAction Stop
                Copy-Item -Path $Source -Destination $destFile -Force -ErrorAction Stop
            } catch {
                Write-Warning "Could not update locked file '$destFile': $_"
            }
        }
    }
}

# 2. Build and copy all Drop-in Parser Plugins into Server, CLI, and Root Parsers directories
Write-Host "`n[3/3] Deploying Drop-in Parser Plugins..." -ForegroundColor Green
$parserProjects = Get-ChildItem "$rootDir/src/Parsers" -Directory | Where-Object { $_.Name.StartsWith("CodeGraphMcp.Parsers.") }

foreach ($proj in $parserProjects) {
    $parserName = $proj.Name.Replace("CodeGraphMcp.Parsers.", "")
    $serverParserDest = Join-Path $serverOut "Parsers\$parserName"
    $cliParserDest = Join-Path $cliOut "Parsers\$parserName"
    $rootParserDest = Join-Path $publishDir "Parsers\$parserName"
    
    Write-Host "  -> Deploying '$parserName' plugin..." -ForegroundColor Gray
    
    # Build the parser plugin project
    dotnet build "$($proj.FullName)\$($proj.Name).csproj" -c $Configuration | Out-Null
    
    $binDir = Join-Path $proj.FullName "bin\$Configuration\net10.0"
    
    # Ensure destination directories exist
    if (-not (Test-Path $serverParserDest)) { New-Item -ItemType Directory -Path $serverParserDest -Force | Out-Null }
    if (-not (Test-Path $cliParserDest)) { New-Item -ItemType Directory -Path $cliParserDest -Force | Out-Null }
    if (-not (Test-Path $rootParserDest)) { New-Item -ItemType Directory -Path $rootParserDest -Force | Out-Null }
    
    # Copy managed assemblies and win-x64 native assets
    $filesToCopy = Get-ChildItem -Path $binDir -File | Where-Object { $_.Extension -in @(".dll", ".pdb", ".json") }
    foreach ($file in $filesToCopy) {
        Safe-CopyFile -Source $file.FullName -Destination $serverParserDest
        Safe-CopyFile -Source $file.FullName -Destination $cliParserDest
        Safe-CopyFile -Source $file.FullName -Destination $rootParserDest
    }
    
    # Copy win-x64 runtimes if present (e.g. tree-sitter native dlls)
    $winRuntime = Join-Path $binDir "runtimes\win-x64\native"
    if (Test-Path $winRuntime) {
        $serverNative = Join-Path $serverParserDest "runtimes\win-x64\native"
        $cliNative = Join-Path $cliParserDest "runtimes\win-x64\native"
        $rootNative = Join-Path $rootParserDest "runtimes\win-x64\native"
        if (-not (Test-Path $serverNative)) { New-Item -ItemType Directory -Path $serverNative -Force | Out-Null }
        if (-not (Test-Path $cliNative)) { New-Item -ItemType Directory -Path $cliNative -Force | Out-Null }
        if (-not (Test-Path $rootNative)) { New-Item -ItemType Directory -Path $rootNative -Force | Out-Null }
        foreach ($nativeFile in (Get-ChildItem -Path "$winRuntime\*" -File)) {
            Safe-CopyFile -Source $nativeFile.FullName -Destination $serverNative
            Safe-CopyFile -Source $nativeFile.FullName -Destination $cliNative
            Safe-CopyFile -Source $nativeFile.FullName -Destination $rootNative
        }
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Publish Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Server Executable : $serverOut\CodeGraphMcp.Server.exe" -ForegroundColor White
Write-Host "CLI Executables   : $cliOut\CodeGraphMcp.exe" -ForegroundColor White
Write-Host "                    $publishDir\CodeGraphMcp.exe" -ForegroundColor White
Write-Host "Plugins Installed : $($parserProjects.Count) language parsers in Parsers/" -ForegroundColor White
