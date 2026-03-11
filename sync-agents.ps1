# sync-agents.ps1
# Copies shared roles and rules from ~/.cursor/shared/ into MigrationHub.

$shared = "$env:USERPROFILE\.cursor\shared"
$project = "$PSScriptRoot\.cursor"

if (!(Test-Path $shared)) {
    Write-Error "Shared agents not found at $shared"
    exit 1
}

Write-Host "Syncing roles..." -ForegroundColor Cyan
Copy-Item "$shared\roles\*.md" "$project\roles\" -Force

Write-Host "Syncing rules..." -ForegroundColor Cyan
Copy-Item "$shared\rules\*.mdc" "$project\rules\" -Force

Write-Host "Done. Project-specific files (MEMEX.md, project.md, agents/*.md) NOT changed." -ForegroundColor Green
