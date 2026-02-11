<#
.SYNOPSIS
  Deletes all Docker Hub tags for vglu/d365fo-deploy-portal except "latest".
.DESCRIPTION
  Uses Docker Hub API. Requires DOCKER_HUB_USER and DOCKER_HUB_PASSWORD env vars
  (account password; PAT does not have delete permission).
  If credentials are not set, prints the list of tags and link for manual delete.
.EXAMPLE
  $env:DOCKER_HUB_USER="vglu"; $env:DOCKER_HUB_PASSWORD="your_password"; .\docker-hub-delete-old-tags.ps1
#>
$ErrorActionPreference = "Stop"
$repo = "vglu/d365fo-deploy-portal"
$keepTag = "latest"

# List tags (no auth)
$listUrl = "https://hub.docker.com/v2/repositories/$repo/tags?page_size=100"
$list = Invoke-RestMethod -Uri $listUrl -Method Get
$tags = $list.results | ForEach-Object { $_.name }

Write-Host "Repository: $repo" -ForegroundColor Cyan
Write-Host "Current tags: $($tags -join ', ')" -ForegroundColor Gray

$toDelete = $tags | Where-Object { $_ -ne $keepTag }
if ($toDelete.Count -eq 0) {
    Write-Host "Nothing to delete (only '$keepTag' or empty)." -ForegroundColor Green
    exit 0
}

$user = $env:DOCKER_HUB_USER
$password = $env:DOCKER_HUB_PASSWORD
if (-not $user -or -not $password) {
    Write-Host ""
    Write-Host "To delete tags automatically, set credentials and re-run:" -ForegroundColor Yellow
    Write-Host '  $env:DOCKER_HUB_USER="vglu"; $env:DOCKER_HUB_PASSWORD="your_password"; .\scripts\docker-hub-delete-old-tags.ps1' -ForegroundColor White
    Write-Host ""
    Write-Host "Or delete manually: https://hub.docker.com/r/$repo/tags" -ForegroundColor Yellow
    Write-Host "Tags to delete: $($toDelete -join ', ')" -ForegroundColor Yellow
    exit 1
}

# Login to get JWT
$loginBody = @{ username = $user; password = $password } | ConvertTo-Json
$loginResponse = Invoke-RestMethod -Uri "https://hub.docker.com/v2/users/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token
$headers = @{ Authorization = "Bearer $token" }

foreach ($tag in $toDelete) {
    $deleteUrl = "https://hub.docker.com/v2/repositories/$repo/tags/$tag/"
    try {
        Invoke-RestMethod -Uri $deleteUrl -Method Delete -Headers $headers
        Write-Host "  Deleted: $tag" -ForegroundColor Green
    } catch {
        Write-Host "  Failed to delete $tag : $_" -ForegroundColor Red
    }
    Start-Sleep -Milliseconds 500
}

Write-Host "Done. Kept: $keepTag" -ForegroundColor Green
