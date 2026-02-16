# Default port 5137 (see launchSettings.json). If "address already in use", another instance is running.
$port = 5137
try {
    $inUse = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($inUse) {
        $pid = $inUse.OwningProcess
        Write-Host "Port $port is already in use (PID $pid). Stop it first: Stop-Process -Id $pid -Force" -ForegroundColor Yellow
        $null = Read-Host "Press Enter to try anyway (will likely fail) or Ctrl+C to cancel"
    }
} catch { <# Get-NetTCPConnection not available (e.g. PowerShell Core on Linux); skip check #> }

dotnet watch --project src/DeployPortal
