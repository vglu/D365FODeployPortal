# Compare Portal/Docker output with reference files.
# Usage:
#   .\Compare-PortalVsReference.ps1 -BaseUrl "http://localhost:5137" -Label "Portal"
#   .\Compare-PortalVsReference.ps1 -BaseUrl "http://localhost:5000" -Label "Container"
#
# Expects: D:\Downloads\SCT\1046703\exp\*.zip (3 LCS), D:\Downloads\SCT\1046703\exp\Joined\P2.zip, D:\Downloads\SCT\1046703\exp\Joined\P2 (folder)
# Output: tables and saved files in D:\Downloads\SCT\1046703\exp\Compare\{Label}\

param(
    [string]$BaseUrl = "http://localhost:5137",
    [string]$Label = "Portal",
    [string]$ExpRoot = "D:\Downloads\SCT\1046703\exp",
    [string]$OutRoot = "D:\Downloads\SCT\1046703\exp\Compare"
)

$ErrorActionPreference = "Stop"
$expPath = $ExpRoot
$joinedPath = Join-Path $expPath "Joined"
$p2Zip = Join-Path $joinedPath "P2.zip"
$p2Folder = Join-Path $joinedPath "P2"
$outDir = Join-Path $OutRoot $Label
$null = New-Item -ItemType Directory -Force -Path $outDir

function Get-Size($path) {
    if (-not (Test-Path $path)) { return $null }
    if (Test-Path $path -PathType Leaf) { return (Get-Item $path).Length }
    (Get-ChildItem $path -Recurse -File | Measure-Object -Property Length -Sum).Sum
}

function Get-FolderZipSize($folderPath) {
    $tempZip = Join-Path $env:TEMP "P2_unified_ref_$(Get-Random).zip"
    try {
        Compress-Archive -Path (Join-Path $folderPath "*") -DestinationPath $tempZip -Force
        return (Get-Item $tempZip).Length
    } finally {
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
    }
}

# Reference sizes
$refP2ZipSize = Get-Size $p2Zip
$refP2FolderZipSize = Get-FolderZipSize $p2Folder
if (-not $refP2ZipSize) { Write-Error "Reference LCS not found: $p2Zip" }
if (-not $refP2FolderZipSize) { Write-Error "Reference Unified (P2 folder) not found: $p2Folder" }

Write-Host "Reference LCS (P2.zip): $refP2ZipSize bytes"
Write-Host "Reference Unified (P2 as zip): $refP2FolderZipSize bytes"
Write-Host ""

# Upload three packages
$zips = @(
    (Join-Path $expPath "AXDeployablePackageAFSPM_2026.2.11.1.zip"),
    (Join-Path $expPath "AXDeployablePackageAL_2026.2.11.1.zip"),
    (Join-Path $expPath "AXDeployablePackagePCM_2026.2.11.1.zip")
)
foreach ($z in $zips) {
    if (-not (Test-Path $z)) { Write-Error "Missing: $z" }
}

$ids = @()
foreach ($z in $zips) {
    $name = [System.IO.Path]::GetFileName($z)
    $uri = "$BaseUrl/api/packages/upload"
    $json = & curl.exe -s -X POST $uri -F "file=@$z" 2>&1
    $r = $json | ConvertFrom-Json
    if (-not $r.id) { Write-Error "Upload failed for $name : $json" }
    $ids += $r.id
    Write-Host "Uploaded $name -> id $($r.id)"
}

# Merge
$mergeBody = @{ packageIds = $ids; mergeName = "Merged_Test" } | ConvertTo-Json
$mergeResult = Invoke-RestMethod -Uri "$BaseUrl/api/packages/merge" -Method Post -Body $mergeBody -ContentType "application/json"
$mergedId = $mergeResult.id
Write-Host "Merged -> id $mergedId"

# Download merged (LCS) - use -OutFile to avoid binary corruption
$mergedPath = Join-Path $outDir "Merged_LCS.zip"
Invoke-WebRequest -Uri "$BaseUrl/api/packages/$mergedId/download" -Method Get -UseBasicParsing -OutFile $mergedPath
$portalJoinedSize = (Get-Item $mergedPath).Length
Write-Host "Downloaded merged LCS: $portalJoinedSize bytes -> $mergedPath"

# Convert to Unified
$convertResult = Invoke-RestMethod -Uri "$BaseUrl/api/packages/$mergedId/convert/unified" -Method Post
$unifiedId = $convertResult.id
Write-Host "Converted to Unified -> id $unifiedId"

# Download unified - use -OutFile to avoid binary corruption
$unifiedPath = Join-Path $outDir "Merged_Unified.zip"
Invoke-WebRequest -Uri "$BaseUrl/api/packages/$unifiedId/download" -Method Get -UseBasicParsing -OutFile $unifiedPath
$portalUnifiedSize = (Get-Item $unifiedPath).Length
Write-Host "Downloaded Unified: $portalUnifiedSize bytes -> $unifiedPath"

# Tables
Write-Host ""
Write-Host "=== JOIN (LCS merged) ==="
Write-Host ("{0,-30} {1,15} {2,15}" -f "FileName", "Size_$Label", "Size_Original")
Write-Host ("{0,-30} {1,15} {2,15}" -f "Merged_LCS.zip", $portalJoinedSize, $refP2ZipSize)
Write-Host ""
Write-Host "=== CONVERTED (Unified) ==="
Write-Host ("{0,-30} {1,15} {2,15}" -f "FileName", "Size_$Label", "Size_Original")
Write-Host ("{0,-30} {1,15} {2,15}" -f "Merged_Unified.zip", $portalUnifiedSize, $refP2FolderZipSize)

# Export for later comparison
@{
    Label = $Label
    BaseUrl = $BaseUrl
    Join = @{ FileName = "Merged_LCS.zip"; PortalSize = $portalJoinedSize; OriginalSize = $refP2ZipSize }
    Converted = @{ FileName = "Merged_Unified.zip"; PortalSize = $portalUnifiedSize; OriginalSize = $refP2FolderZipSize }
    MergedPath = $mergedPath
    UnifiedPath = $unifiedPath
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir "report.json") -Encoding UTF8

Write-Host ""
Write-Host "Report saved to $outDir\report.json"
