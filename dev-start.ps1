<#
.SYNOPSIS
    Starts all three ADF Agent Monitor services in separate console windows.

.DESCRIPTION
    Launches Worker, Api, and Dashboard as child processes, each in its own
    PowerShell window so logs remain readable independently.

    Press Enter in THIS window to send Stop-Process to all three children and exit.
    You can also close any child window individually without affecting the others.

.NOTES
    API port  : https://localhost:7059  /  http://localhost:5070  (from Api launchSettings.json)
    Dashboard : https://localhost:7071  /  http://localhost:5071  (set via --urls below)

    ⚠ If you have not yet set user-secrets, run these first:
        dotnet user-secrets --project src/AdfAgentMonitor.Api set "Api:ApiKey" "<secret>"
        dotnet user-secrets --project src/AdfAgentMonitor.Api set "Anthropic:ApiKey" "<key>"
        (see README.md for the full list)

    ⚠ The Dashboard wwwroot/appsettings.json currently points ApiBaseUrl at
      https://localhost:7001 — update it (or use Settings → Connection in the UI)
      to https://localhost:7059 to match the Api port above.
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ---------------------------------------------------------------------------
# Service definitions
# ---------------------------------------------------------------------------
$services = @(
    [PSCustomObject]@{
        Name    = 'ADF Worker'
        Project = 'src\AdfAgentMonitor.Worker'
        Urls    = $null                                        # Worker has no HTTP server
        Color   = 'Cyan'
    },
    [PSCustomObject]@{
        Name    = 'ADF Api'
        Project = 'src\AdfAgentMonitor.Api'
        Urls    = 'https://localhost:7059;http://localhost:5070'
        Color   = 'Yellow'
    },
    [PSCustomObject]@{
        Name    = 'ADF Dashboard'
        Project = 'src\AdfAgentMonitor.Dashboard'
        Urls    = 'https://localhost:7071;http://localhost:5071'
        Color   = 'Green'
    }
)

# ---------------------------------------------------------------------------
# Launch each service in its own window
# ---------------------------------------------------------------------------
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

foreach ($svc in $services) {
    $urlsArg = if ($svc.Urls) { " --urls `"$($svc.Urls)`"" } else { '' }
    $runCmd  = "dotnet run --project `"$root\$($svc.Project)`"$urlsArg"

    # Set the child window title then run the project
    $psCmd = "`$host.UI.RawUI.WindowTitle = '$($svc.Name)'; $runCmd"

    $proc = Start-Process powershell `
        -ArgumentList '-NoExit', '-Command', $psCmd `
        -PassThru

    $processes.Add($proc)
    Write-Host "  ✓ Started $($svc.Name)  (PID $($proc.Id))" -ForegroundColor $svc.Color
}

# ---------------------------------------------------------------------------
# Print URLs and wait
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host '  ADF Agent Monitor — all services running' -ForegroundColor White
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host '  Api       https://localhost:7059' -ForegroundColor Yellow
Write-Host '  Api (http) http://localhost:5070' -ForegroundColor Yellow
Write-Host '  Dashboard https://localhost:7071' -ForegroundColor Green
Write-Host '  Dashboard (http) http://localhost:5071' -ForegroundColor Green
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host ''
Write-Host '  Press Enter to stop all services and exit.' -ForegroundColor DarkGray
Write-Host ''

try {
    Read-Host | Out-Null
}
finally {
    # ---------------------------------------------------------------------------
    # Graceful shutdown — stop children regardless of how we exit
    # ---------------------------------------------------------------------------
    Write-Host 'Stopping services...' -ForegroundColor Red

    foreach ($proc in $processes) {
        if (-not $proc.HasExited) {
            # Send Ctrl+C to dotnet (graceful) then force-kill after 5 s if needed
            try { $proc.CloseMainWindow() | Out-Null } catch { }

            if (-not $proc.WaitForExit(5000)) {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Write-Host 'All services stopped.' -ForegroundColor Red
}
