<#
.SYNOPSIS
    CodeAtlas Relay Daemon Service
.DESCRIPTION
    Lightweight PowerShell background daemon that provides local HTTP endpoint (localhost:9123)
    for inter-process messaging between AGY and Copilot without cross-directory access prompts.
#>

param (
    [int]$Port = 9123
)

$ErrorActionPreference = "Stop"
$LogPath = "$env:LOCALAPPDATA\CodeAtlasRelay\relay.log"
$LogDir = [System.IO.Path]::GetDirectoryName($LogPath)
if (-not (Test-Path -Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "[$timestamp] $Message" | Out-File -FilePath $LogPath -Append -Encoding utf8
}

Write-Log "Starting CodeAtlas Relay Daemon on port $Port..."

# Recipient-targeted queue dictionary
$queues = @{
    agy       = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    copilot   = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    broadcast = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
}

$listener = New-Object System.Net.HttpListener
$prefix = "http://localhost:$Port/"
$listener.Prefixes.Add($prefix)

try {
    $listener.Start()
    Write-Log "HttpListener active on $prefix"
}
catch {
    Write-Log "ERROR starting HttpListener: $_"
    exit 1
}

# Register graceful exit on PowerShell process exit
$null = [System.AppDomain]::CurrentDomain.add_ProcessExit({
    Write-Log "Stopping CodeAtlas Relay Daemon..."
    $listener.Stop()
    $listener.Close()
})

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response

        # CORS Headers for IDE web view contexts
        $response.Headers.Add("Access-Control-Allow-Origin", "*")
        $response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        $response.Headers.Add("Access-Control-Allow-Headers", "Content-Type")

        if ($request.HttpMethod -eq "OPTIONS") {
            $response.StatusCode = 204
            $response.Close()
            continue
        }

        $path = $request.Url.AbsolutePath.ToLower()

        switch ($path) {
            "/health" {
                $response.ContentType = "application/json"
                $status = @{
                    status     = "ok"
                    uptime     = (Get-Date).ToString("o")
                    queueDepth = @{
                        agy       = $queues["agy"].Count
                        copilot   = $queues["copilot"].Count
                        broadcast = $queues["broadcast"].Count
                    }
                } | ConvertTo-Json
                $buffer = [System.Text.Encoding]::UTF8.GetBytes($status)
                $response.OutputStream.Write($buffer, 0, $buffer.Length)
            }

            "/send" {
                if ($request.HttpMethod -eq "POST") {
                    $reader = New-Object System.IO.StreamReader($request.InputStream, $request.ContentEncoding)
                    $body = $reader.ReadToEnd()
                    $reader.Close()

                    if (-not [string]::IsNullOrWhiteSpace($body)) {
                        $target = "broadcast"
                        try {
                            $jsonObj = $body | ConvertFrom-Json
                            if ($jsonObj.to) {
                                $t = $jsonObj.to.ToString().ToLower()
                                if ($queues.ContainsKey($t)) {
                                    $target = $t
                                }
                            }
                        } catch { }

                        $queues[$target].Enqueue($body)
                        Write-Log "Enqueued message to '$target'. Queue depth: $($queues[$target].Count)"
                    }

                    $response.ContentType = "application/json"
                    $respJson = @{ success = $true; target = $target } | ConvertTo-Json
                    $buffer = [System.Text.Encoding]::UTF8.GetBytes($respJson)
                    $response.OutputStream.Write($buffer, 0, $buffer.Length)
                } else {
                    $response.StatusCode = 405
                }
            }

            "/receive" {
                if ($request.HttpMethod -eq "GET") {
                    $response.ContentType = "application/json"
                    $client = $request.QueryString["client"]
                    if ([string]::IsNullOrWhiteSpace($client)) {
                        $client = "broadcast"
                    } else {
                        $client = $client.ToLower()
                    }

                    $msg = $null
                    $dequeued = $false

                    # Check client-specific queue first
                    if ($queues.ContainsKey($client) -and $queues[$client].TryDequeue([ref]$msg)) {
                        $dequeued = $true
                    }
                    # Fallback to broadcast queue if no targeted message
                    elseif ($client -ne "broadcast" -and $queues["broadcast"].TryDequeue([ref]$msg)) {
                        $dequeued = $true
                    }

                    if ($dequeued) {
                        $buffer = [System.Text.Encoding]::UTF8.GetBytes($msg)
                    } else {
                        $emptyJson = @{ messages = @(); status = "empty" } | ConvertTo-Json
                        $buffer = [System.Text.Encoding]::UTF8.GetBytes($emptyJson)
                    }
                    $response.OutputStream.Write($buffer, 0, $buffer.Length)
                } else {
                    $response.StatusCode = 405
                }
            }

            default {
                $response.StatusCode = 404
            }
        }

        $response.Close()
    }
    catch {
        Write-Log "Error handling request: $_"
    }
}
