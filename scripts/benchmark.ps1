param(
    [int]$Iterations = 25,
    [switch]$IncludeSyntheticCorpus
)

$ErrorActionPreference = "Stop"

function Get-LatencySummary {
    param([double[]]$Samples)

    # 1. Declarations
    $sorted  = $null
    $count   = 0
    $midIdx  = 0
    $median  = 0.0
    $p95Idx  = 0
    $meanVal = 0.0

    # 2. Preconditions
    if (-not $Samples -or $Samples.Count -eq 0) {
        throw "Samples collection must contain at least one measurement."
    }

    # 3. Narrative
    $sorted  = @($Samples | Sort-Object)
    $count   = $sorted.Count
    $midIdx  = [int][math]::Floor($count / 2)
    $median  = if ($count % 2 -eq 1) { $sorted[$midIdx] } else { ($sorted[$midIdx - 1] + $sorted[$midIdx]) / 2.0 }
    $p95Idx  = [math]::Min($count - 1, [int][math]::Ceiling($count * 0.95) - 1)
    $meanVal = ($sorted | Measure-Object -Average).Average

    # 4. Output
    return [pscustomobject]@{
        MinMs    = [math]::Round($sorted[0], 2)
        MedianMs = [math]::Round($median, 2)
        MeanMs   = [math]::Round($meanVal, 2)
        P95Ms    = [math]::Round($sorted[$p95Idx], 2)
    }
}

function Send-McpRequest {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$Id,
        [string]$ToolName,
        [hashtable]$Arguments
    )

    # 1. Declarations
    $payload  = ""
    $response = ""
    $sw       = [System.Diagnostics.Stopwatch]::new()

    # 2. Preconditions
    if ($null -eq $Process -or $Process.HasExited) {
        throw "MCP server process is not running."
    }

    # 3. Narrative
    $payload = (@{
        jsonrpc = "2.0"
        id      = $Id
        method  = "tools/call"
        params  = @{
            name      = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Compress -Depth 6)

    $sw.Start()
    $Process.StandardInput.WriteLine($payload)
    $Process.StandardInput.Flush()
    $response = $Process.StandardOutput.ReadLine()
    $sw.Stop()

    # 4. Output
    return [pscustomobject]@{
        ElapsedMs = $sw.Elapsed.TotalMilliseconds
        Response  = $response
    }
}

function Stop-ProcessQuietly {
    param([System.Diagnostics.Process]$Process)

    if ($null -ne $Process -and -not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Measure-McpDaemonSuite {
    param(
        [string]$McpExe,
        [string]$RepoPath,
        [string]$DbDir,
        [string]$TargetSymbol,
        [string]$CallerSymbol,
        [string]$SyncRelFile,
        [int]$RunCount
    )

    # 1. Declarations
    $psi         = $null
    $proc        = $null
    $reqId       = 10
    $warmCalls   = @()
    $defTimes    = [System.Collections.Generic.List[double]]::new()
    $callerTimes = [System.Collections.Generic.List[double]]::new()
    $searchTimes = [System.Collections.Generic.List[double]]::new()
    $syncTimes   = [System.Collections.Generic.List[double]]::new()
    $ramMb       = 0.0

    # 2. Preconditions
    if (-not (Test-Path $McpExe)) {
        throw "MCP executable not found at '$McpExe'. Run ./scripts/publish.ps1 first."
    }

    # 3. Narrative
    $psi = [System.Diagnostics.ProcessStartInfo]::new($McpExe, "--db-dir `"$DbDir`"")
    $psi.WorkingDirectory       = $RepoPath
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardInput  = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $null = $proc.StandardError.ReadToEndAsync()

        $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"benchmark","version":"1.0"}}}')
        $proc.StandardInput.Flush()
        $null = $proc.StandardOutput.ReadLine()

        $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
        $proc.StandardInput.Flush()

        $warmCalls = @(
            @{ Name = "GetSymbolDefinition"; Args = @{ symbolName = $TargetSymbol; targetDirectory = $RepoPath } },
            @{ Name = "FindSymbolCallers";   Args = @{ symbolName = $CallerSymbol; targetDirectory = $RepoPath } },
            @{ Name = "SearchCodebaseGraph"; Args = @{ pattern = $TargetSymbol; limit = 20; targetDirectory = $RepoPath } },
            @{ Name = "SyncFile";            Args = @{ filePath = $SyncRelFile; targetDirectory = $RepoPath } }
        )

        foreach ($call in $warmCalls) {
            for ($w = 0; $w -lt 3; $w++) {
                $reqId++
                $res = Send-McpRequest -Process $proc -Id $reqId -ToolName $call.Name -Arguments $call.Args
                $parsed = $res.Response | ConvertFrom-Json
                if ($parsed.error -or $parsed.result.isError) {
                    throw "MCP tool '$($call.Name)' returned an error: $($res.Response)"
                }
            }
        }

        for ($i = 0; $i -lt $RunCount; $i++) {
            $reqId++; $defTimes.Add((Send-McpRequest -Process $proc -Id $reqId -ToolName "GetSymbolDefinition" -Arguments @{ symbolName = $TargetSymbol; targetDirectory = $RepoPath }).ElapsedMs)
            $reqId++; $callerTimes.Add((Send-McpRequest -Process $proc -Id $reqId -ToolName "FindSymbolCallers"   -Arguments @{ symbolName = $CallerSymbol; targetDirectory = $RepoPath }).ElapsedMs)
            $reqId++; $searchTimes.Add((Send-McpRequest -Process $proc -Id $reqId -ToolName "SearchCodebaseGraph" -Arguments @{ pattern = $TargetSymbol; limit = 20; targetDirectory = $RepoPath }).ElapsedMs)
            $reqId++; $syncTimes.Add((Send-McpRequest -Process $proc -Id $reqId -ToolName "SyncFile"              -Arguments @{ filePath = $SyncRelFile; targetDirectory = $RepoPath }).ElapsedMs)
        }

        $proc.Refresh()
        $ramMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    }
    finally {
        Stop-ProcessQuietly -Process $proc
    }

    # 4. Output
    return [pscustomobject]@{
        WorkingSetMb        = $ramMb
        GetSymbolDefinition = Get-LatencySummary -Samples $defTimes
        FindSymbolCallers   = Get-LatencySummary -Samples $callerTimes
        SearchCodebaseGraph = Get-LatencySummary -Samples $searchTimes
        SyncFile            = Get-LatencySummary -Samples $syncTimes
    }
}

function Invoke-CorpusBenchmark {
    param(
        [string]$Label,
        [string]$CliExe,
        [string]$McpExe,
        [string]$RepoPath,
        [string]$DbDir,
        [string]$TargetSymbol,
        [string]$CallerSymbol,
        [string]$SyncRelFile,
        [int]$RunCount
    )

    # 1. Declarations
    $sw            = [System.Diagnostics.Stopwatch]::new()
    $coldOut       = $null
    $coldWallMs    = 0.0
    $noopOut       = $null
    $noopWallMs    = 0.0
    $dbFile        = $null
    $dbSizeKb      = 0.0
    $cliTimes      = [System.Collections.Generic.List[double]]::new()
    $grepTimes     = [System.Collections.Generic.List[double]]::new()
    $mcpStats      = $null

    # 2. Preconditions
    if (Test-Path $DbDir) { Remove-Item $DbDir -Recurse -Force }
    New-Item -ItemType Directory -Path $DbDir -Force | Out-Null

    # 3. Narrative
    Write-Host "`n=== $Label ===" -ForegroundColor Cyan

    $sw.Restart()
    $coldOut = & $CliExe index $RepoPath --db-dir $DbDir --force-full
    $sw.Stop()
    $coldWallMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)

    $sw.Restart()
    $noopOut = & $CliExe index $RepoPath --db-dir $DbDir
    $sw.Stop()
    $noopWallMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)

    $dbFile   = Get-ChildItem $DbDir -Filter "*.db" | Select-Object -First 1
    $dbSizeKb = [math]::Round($dbFile.Length / 1KB, 1)

    for ($i = 0; $i -lt 5; $i++) {
        $sw.Restart()
        $null = & $CliExe query $TargetSymbol -r $RepoPath --db-dir $DbDir
        $sw.Stop()
        $cliTimes.Add($sw.Elapsed.TotalMilliseconds)

        $sw.Restart()
        $null = Get-ChildItem -Path $RepoPath -Recurse -File -Exclude *.dll,*.exe,*.pdb |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|publish)\\' } |
            Select-String -Pattern $TargetSymbol -SimpleMatch
        $sw.Stop()
        $grepTimes.Add($sw.Elapsed.TotalMilliseconds)
    }

    $mcpStats = Measure-McpDaemonSuite `
        -McpExe $McpExe `
        -RepoPath $RepoPath `
        -DbDir $DbDir `
        -TargetSymbol $TargetSymbol `
        -CallerSymbol $CallerSymbol `
        -SyncRelFile $SyncRelFile `
        -RunCount $RunCount

    # 4. Output
    Write-Host ($coldOut | Out-String).Trim()
    Write-Host "Cold Full Wall-Clock : ${coldWallMs} ms"
    Write-Host ($noopOut | Out-String).Trim()
    Write-Host "No-Op Wall-Clock     : ${noopWallMs} ms"
    Write-Host "SQLite DB Footprint  : ${dbSizeKb} KB"
    Write-Host "MCP Daemon RAM       : $($mcpStats.WorkingSetMb) MB"
    Write-Host "CLI One-Shot Query   : $($cliTimes | ForEach-Object { $_ } | Out-Null; (Get-LatencySummary -Samples $cliTimes) | ConvertTo-Json -Compress)"
    Write-Host "Select-String Scan   : $((Get-LatencySummary -Samples $grepTimes) | ConvertTo-Json -Compress)"
    Write-Host "MCP Warm Tool Calls  : $($mcpStats | ConvertTo-Json -Depth 3)"
}

function New-SyntheticMultiLanguageCorpus {
    param([string]$TargetDir)

    # 1. Declarations
    $modDir    = ""
    $webDir    = ""
    $idx       = 0
    $prevIdx   = 0
    $csContent = ""
    $tsContent = ""

    # 2. Preconditions
    if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

    # 3. Narrative
    for ($m = 1; $m -le 20; $m++) {
        $modDir = Join-Path $TargetDir "services\Service$m"
        $webDir = Join-Path $TargetDir "web\module-$m"
        New-Item -ItemType Directory -Path $modDir -Force | Out-Null
        New-Item -ItemType Directory -Path $webDir -Force | Out-Null

        for ($f = 1; $f -le 50; $f++) {
            $idx     = ($m - 1) * 50 + $f
            $prevIdx = [math]::Max(1, $idx - 1)
            $csContent = @"
using System;
using System.Threading.Tasks;

namespace Benchmark.Services.Module$m;

public sealed record OrderRequestDto$idx(int OrderId, string CustomerCode, decimal TotalAmount);

public sealed class OrderWorkflowService$idx
{
    public OrderRequestDto$idx CreateRequest(int id) => new(id, "CUST-$idx", 99.95m);

    public async Task<bool> ExecuteOrderAsync(OrderRequestDto$idx request)
    {
        await Task.Yield();
        var prev = OrderHelper$prevIdx.ValidateCode(request.CustomerCode);
        return prev && request.TotalAmount > 0;
    }
}

public static class OrderHelper$idx
{
    public static bool ValidateCode(string code) => !string.IsNullOrWhiteSpace(code);
}
"@
            [System.IO.File]::WriteAllText((Join-Path $modDir "OrderWorkflow$idx.cs"), $csContent)

            $tsContent = @"
export interface OrderRequestDto$idx {
    orderId: number;
    customerCode: string;
    totalAmount: number;
}

export class OrderPanelComponent$idx {
    public submitOrder(dto: OrderRequestDto$idx): boolean {
        return dto.totalAmount > 0 && dto.customerCode.length > 0;
    }
}
"@
            [System.IO.File]::WriteAllText((Join-Path $webDir "order-panel-$idx.component.ts"), $tsContent)
        }
    }

    Push-Location $TargetDir
    try {
        git -c core.autocrlf=false init -q
        git config user.email "benchmark@codeatlas.local"
        git config user.name "CodeAtlas Benchmark"
        git -c core.autocrlf=false add .
        git commit -m "Initialise deterministic 2,000-file benchmark corpus" -q
    }
    finally {
        Pop-Location
    }
}

# ── Top-Level Benchmark Orchestrator ──────────────────────────────────────────
$rootDir = (Resolve-Path "$PSScriptRoot/..").Path
$cliExe  = Join-Path $rootDir "publish\cli\CodeAtlas.exe"
$mcpExe  = Join-Path $rootDir "publish\server\CodeAtlas.Mcp.exe"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("codeatlas-bench-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))

if (-not (Test-Path $cliExe) -or -not (Test-Path $mcpExe)) {
    throw "Published binaries not found under '$rootDir\publish'. Run 'pwsh -File ./scripts/publish.ps1' before running benchmarks."
}

try {
    Invoke-CorpusBenchmark `
        -Label "Corpus 1: CodeAtlas Self-Hosted Repository" `
        -CliExe $cliExe `
        -McpExe $mcpExe `
        -RepoPath $rootDir `
        -DbDir (Join-Path $tempDir "db-self") `
        -TargetSymbol "IndexOrchestrator" `
        -CallerSymbol "NormalizePath" `
        -SyncRelFile "src/CodeAtlas.Core/Indexer/IndexOrchestrator.cs" `
        -RunCount $Iterations

    if ($IncludeSyntheticCorpus) {
        $synRepo = Join-Path $tempDir "syn-2k"
        New-SyntheticMultiLanguageCorpus -TargetDir $synRepo

        Invoke-CorpusBenchmark `
            -Label "Corpus 2: Deterministic 2,000-File Multi-Language Corpus (C# + TypeScript)" `
            -CliExe $cliExe `
            -McpExe $mcpExe `
            -RepoPath $synRepo `
            -DbDir (Join-Path $tempDir "db-syn") `
            -TargetSymbol "OrderWorkflowService500" `
            -CallerSymbol "ValidateCode" `
            -SyncRelFile "services/Service10/OrderWorkflow500.cs" `
            -RunCount $Iterations
    }
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
