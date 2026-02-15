<#
.SYNOPSIS
    Full round-trip test with Docker: LCS -> Unified -> LCS, then compare the two LCS packages.
.DESCRIPTION
    1. Ensures Docker image and container are running.
    2. Creates a minimal nested LCS package (or uses -LcsPath if provided).
    3. Uploads LCS via API, converts to Unified, converts back to LCS.
    4. Downloads original and round-trip LCS, unzips and compares structure.
.EXAMPLE
    .\test-roundtrip-docker.ps1
    .\test-roundtrip-docker.ps1 -LcsPath "D:\Downloads\MyPackage.zip"
#>
param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$LcsPath = "",  # If set, use this LCS zip instead of creating minimal one
    [switch]$NoBuild,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$TestDir = Join-Path $env:TEMP "DeployPortal-RoundTrip-$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -Path $TestDir -ItemType Directory -Force | Out-Null

function Write-Step { param($Msg) Write-Host "`n--- $Msg ---" -ForegroundColor Cyan }
function Write-Ok    { param($Msg) Write-Host "  OK: $Msg" -ForegroundColor Green }
function Write-Warn  { param($Msg) Write-Host "  WARN: $Msg" -ForegroundColor Yellow }
function Write-Fail  { param($Msg) Write-Host "  FAIL: $Msg" -ForegroundColor Red }

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  ROUND-TRIP TEST (LCS -> Unified -> LCS)" -ForegroundColor Cyan
Write-Host "  Output: $TestDir" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# ----- 1. Docker -----
Write-Step "Docker: image and container"
if (-not $NoBuild) {
    docker build -t vglu/d365fo-deploy-portal:latest (Get-Location) 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail "Docker build failed"; exit 1 }
    Write-Ok "Image built"
}
$running = docker ps -q -f name=deploy-portal
if (-not $running) {
    docker compose up -d 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail "docker compose up failed"; exit 1 }
    Write-Ok "Container started"
    Start-Sleep -Seconds 8
} else {
    Write-Ok "Container already running"
}

# Wait for API
$maxAttempts = 15
for ($i = 0; $i -lt $maxAttempts; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/api/packages" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { break }
    } catch {}
    if ($i -eq $maxAttempts - 1) { Write-Fail "API not ready at $BaseUrl"; exit 1 }
    Start-Sleep -Seconds 2
}
Write-Ok "API ready"

# ----- 2. LCS package -----
Write-Step "LCS package"
if ($LcsPath -and (Test-Path $LcsPath)) {
    $lcsZipPath = $LcsPath
    Write-Ok "Using provided LCS: $LcsPath"
} else {
    $lcsZipPath = Join-Path $TestDir "input_lcs.zip"
    $rootFolder = "AX_Minimal_Test_1.0.0.0"
    $moduleZipPath = Join-Path $TestDir "dynamicsax-ModuleA.1.0.0.0.zip"
    [System.IO.Compression.ZipFile]::Open($moduleZipPath, [System.IO.Compression.ZipArchiveMode]::Create) | ForEach-Object {
        $e = $_.CreateEntry("ModuleA/metadata.xml")
        $w = New-Object System.IO.StreamWriter($e.Open())
        $w.Write("<metadata/>")
        $w.Close()
        $_.Dispose()
    }
    [System.IO.Compression.ZipFile]::Open($lcsZipPath, [System.IO.Compression.ZipArchiveMode]::Create) | ForEach-Object {
        # Root-level marker so PackageAnalyzer.DetectPackageType returns "LCS" (looks for AOSService/ or HotfixInstallationInfo.xml at root)
        $_.CreateEntry("AOSService/Packages/files/.lcs").Open().Close()
        $hotfix = "<?xml version=`"1.0`"?><HotfixInstallationInfo><PlatformVersion>10.0.0.0</PlatformVersion><MetadataModuleList><string>ModuleA</string></MetadataModuleList></HotfixInstallationInfo>"
        $e = $_.CreateEntry("$rootFolder/HotfixInstallationInfo.xml")
        $w = New-Object System.IO.StreamWriter($e.Open())
        $w.Write($hotfix)
        $w.Close()
        $moduleBytes = [System.IO.File]::ReadAllBytes($moduleZipPath)
        $e2 = $_.CreateEntry("$rootFolder/AOSService/Packages/files/dynamicsax-ModuleA.1.0.0.0.zip")
        $str = $e2.Open()
        $str.Write($moduleBytes, 0, $moduleBytes.Length)
        $str.Close()
        $_.Dispose()
    }
    Remove-Item $moduleZipPath -Force -ErrorAction SilentlyContinue
    Write-Ok "Created minimal LCS: $lcsZipPath"
}

# ----- 3. Upload -----
Write-Step "Upload LCS"
# Use curl.exe for multipart upload (works in PowerShell 5.1 where -Form is not available)
$uploadJsonPath = Join-Path $TestDir "upload_response.json"
& curl.exe -s -m 600 -X POST -F "file=@$lcsZipPath" "$BaseUrl/api/packages/upload" -o $uploadJsonPath
$uploadRaw = Get-Content $uploadJsonPath -Raw -ErrorAction SilentlyContinue
if (-not $uploadRaw -or $uploadRaw.Trim() -eq "") {
    Write-Fail "Upload failed (empty response)"
    exit 1
}
try {
    $uploadResponse = $uploadRaw | ConvertFrom-Json
} catch {
    Write-Fail "Upload response is not JSON. Response: $($uploadRaw.Substring(0, [Math]::Min(500, $uploadRaw.Length)))..."
    exit 1
}
$idLcs = $uploadResponse.id
if (-not $idLcs) { Write-Fail "Upload response had no id"; Write-Host $uploadRaw; exit 1 }
Write-Ok "Uploaded LCS package Id = $idLcs"

# ----- 4. LCS -> Unified -----
Write-Step "Convert LCS -> Unified"
$unifiedResponse = Invoke-RestMethod -Uri "$BaseUrl/api/packages/$idLcs/convert/unified" -Method Post
$idUnified = $unifiedResponse.id
Write-Ok "Unified package Id = $idUnified"

# ----- 5. Unified -> LCS -----
Write-Step "Convert Unified -> LCS"
$lcsBackResponse = Invoke-RestMethod -Uri "$BaseUrl/api/packages/$idUnified/convert/lcs" -Method Post
$idLcsBack = $lcsBackResponse.id
Write-Ok "Round-trip LCS package Id = $idLcsBack"

# ----- 6. Download both LCS -----
Write-Step "Download packages"
$originalZip = Join-Path $TestDir "original_lcs.zip"
$roundtripZip = Join-Path $TestDir "roundtrip_lcs.zip"
Invoke-WebRequest -Uri "$BaseUrl/api/packages/$idLcs/download" -OutFile $originalZip -UseBasicParsing
Invoke-WebRequest -Uri "$BaseUrl/api/packages/$idLcsBack/download" -OutFile $roundtripZip -UseBasicParsing
Write-Ok "Saved original and round-trip LCS"

# ----- 7. Compare -----
Write-Step "Compare LCS packages"
$extractOrig = Join-Path $TestDir "extract_original"
$extractRound = Join-Path $TestDir "extract_roundtrip"
New-Item $extractOrig -ItemType Directory -Force | Out-Null
New-Item $extractRound -ItemType Directory -Force | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($originalZip, $extractOrig)
[System.IO.Compression.ZipFile]::ExtractToDirectory($roundtripZip, $extractRound)

function Get-ZipEntryPaths($zipPath) {
    $list = New-Object System.Collections.Generic.List[string]
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    foreach ($e in $zip.Entries) { $list.Add($e.FullName.TrimEnd('/')) }
    $zip.Dispose()
    $list | Where-Object { $_ -ne "" } | Sort-Object
}

function Get-ZipEntriesWithSize($zipPath) {
    $hash = @{}
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    foreach ($e in $zip.Entries) {
        $name = $e.FullName.TrimEnd('/')
        if ($name -eq "") { continue }
        $hash[$name] = $e.Length
    }
    $zip.Dispose()
    $hash
}

$entriesOrig = Get-ZipEntryPaths $originalZip
$entriesRound = Get-ZipEntryPaths $roundtripZip
$entriesWithSizeOrig = Get-ZipEntriesWithSize $originalZip
$entriesWithSizeRound = Get-ZipEntriesWithSize $roundtripZip
$totalSizeOrig = ($entriesWithSizeOrig.Values | Measure-Object -Sum).Sum
$totalSizeRound = ($entriesWithSizeRound.Values | Measure-Object -Sum).Sum

$onlyInOrig = $entriesOrig | Where-Object { $_ -notin $entriesRound }
$onlyInRound = $entriesRound | Where-Object { $_ -notin $entriesOrig }
$common = $entriesOrig | Where-Object { $_ -in $entriesRound }

Write-Host "  Original LCS entries:    $($entriesOrig.Count)" -ForegroundColor White
Write-Host "  Round-trip LCS entries: $($entriesRound.Count)" -ForegroundColor White
Write-Host "  Common entries:         $($common.Count)" -ForegroundColor White
if ($onlyInOrig.Count -gt 0) {
    Write-Warn "Only in original ($($onlyInOrig.Count)):"
    $onlyInOrig | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
    if ($onlyInOrig.Count -gt 20) { Write-Host "    ... and $($onlyInOrig.Count - 20) more" }
}
if ($onlyInRound.Count -gt 0) {
    Write-Warn "Only in round-trip ($($onlyInRound.Count)):"
    $onlyInRound | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
    if ($onlyInRound.Count -gt 20) { Write-Host "    ... and $($onlyInRound.Count - 20) more" }
}

$hasHotfixOrig = Test-Path (Join-Path $extractOrig "*\HotfixInstallationInfo.xml") -PathType Leaf
$hasHotfixRound = Get-ChildItem $extractRound -Recurse -Filter "HotfixInstallationInfo.xml" -ErrorAction SilentlyContinue | Select-Object -First 1
$hasAosOrig = Get-ChildItem $extractOrig -Recurse -Directory -Filter "AOSService" -ErrorAction SilentlyContinue | Select-Object -First 1
$hasAosRound = Get-ChildItem $extractRound -Recurse -Directory -Filter "AOSService" -ErrorAction SilentlyContinue | Select-Object -First 1
$hasFilesOrig = Get-ChildItem $extractOrig -Recurse -Filter "dynamicsax-*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
$hasFilesRound = Get-ChildItem $extractRound -Recurse -Filter "dynamicsax-*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1

# Scripts: file count (original often has exe, dll, .ps1; round-trip uses template = often only License)
$scriptsOrig = Get-ChildItem $extractOrig -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -replace [regex]::Escape($extractOrig), "" -match "Scripts" }
$scriptsRound = Get-ChildItem $extractRound -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -replace [regex]::Escape($extractRound), "" -match "Scripts" }
$scriptsOrigCount = ($scriptsOrig | Measure-Object).Count
$scriptsRoundCount = ($scriptsRound | Measure-Object).Count
# .nupkg in Packages: converter does NOT emit .nupkg (only dynamicsax-*.zip)
$nupkgOrig = Get-ChildItem $extractOrig -Recurse -Filter "*.nupkg" -ErrorAction SilentlyContinue
$nupkgRound = Get-ChildItem $extractRound -Recurse -Filter "*.nupkg" -ErrorAction SilentlyContinue
$nupkgOrigCount = ($nupkgOrig | Measure-Object).Count
$nupkgRoundCount = ($nupkgRound | Measure-Object).Count

Write-Host ""
Write-Host "  Structure check:" -ForegroundColor White
Write-Host "    Original:   HotfixInstallationInfo=$hasHotfixOrig, AOSService=$($null -ne $hasAosOrig), dynamicsax-*.zip=$($null -ne $hasFilesOrig)"
Write-Host "    Round-trip: HotfixInstallationInfo=$($null -ne $hasHotfixRound), AOSService=$($null -ne $hasAosRound), dynamicsax-*.zip=$($null -ne $hasFilesRound)"
Write-Host "  Total size (uncompressed): Original=$([math]::Round($totalSizeOrig/1MB, 2)) MB  Round-trip=$([math]::Round($totalSizeRound/1MB, 2)) MB" -ForegroundColor White
Write-Host "  Content:" -ForegroundColor White
Write-Host "    Scripts files:  Original=$scriptsOrigCount  Round-trip=$scriptsRoundCount" -ForegroundColor Gray
Write-Host "    .nupkg in Packages:  Original=$nupkgOrigCount  Round-trip=$nupkgRoundCount" -ForegroundColor Gray

$reportPath = Join-Path $TestDir "comparison_report.txt"
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("==============================================")
[void]$sb.AppendLine("  ROUND-TRIP COMPARISON REPORT (LCS -> Unified -> LCS)")
[void]$sb.AppendLine("==============================================")
[void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("BaseUrl: $BaseUrl")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- SUMMARY ---")
[void]$sb.AppendLine("  Original LCS:    $($entriesOrig.Count) entries, $([math]::Round($totalSizeOrig/1MB, 2)) MB (uncompressed)")
[void]$sb.AppendLine("  Round-trip LCS:  $($entriesRound.Count) entries, $([math]::Round($totalSizeRound/1MB, 2)) MB (uncompressed)")
[void]$sb.AppendLine("  Common paths:    $($common.Count)")
[void]$sb.AppendLine("  Only in original:    $($onlyInOrig.Count)")
[void]$sb.AppendLine("  Only in round-trip:  $($onlyInRound.Count)")
[void]$sb.AppendLine("  Scripts files:   Original=$scriptsOrigCount  Round-trip=$scriptsRoundCount")
[void]$sb.AppendLine("  .nupkg in Packages: Original=$nupkgOrigCount   Round-trip=$nupkgRoundCount")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- ONLY IN ORIGINAL (path + size bytes) ---")
foreach ($p in ($onlyInOrig | Sort-Object)) {
    $sz = $entriesWithSizeOrig[$p]
    [void]$sb.AppendLine("  $p  |  $sz")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- ONLY IN ROUND-TRIP (path + size bytes) ---")
foreach ($p in ($onlyInRound | Sort-Object)) {
    $sz = $entriesWithSizeRound[$p]
    [void]$sb.AppendLine("  $p  |  $sz")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- IN BOTH (path | size_original | size_roundtrip | same_size?) ---")
foreach ($p in ($common | Sort-Object)) {
    $s1 = $entriesWithSizeOrig[$p]
    $s2 = $entriesWithSizeRound[$p]
    $same = if ($s1 -eq $s2) { "SAME" } else { "DIFF" }
    [void]$sb.AppendLine("  $p  |  $s1  |  $s2  |  $same")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- FULL LIST ORIGINAL (path | size) ---")
foreach ($p in ($entriesOrig | Sort-Object)) {
    [void]$sb.AppendLine("  $p  |  $($entriesWithSizeOrig[$p])")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("--- FULL LIST ROUND-TRIP (path | size) ---")
foreach ($p in ($entriesRound | Sort-Object)) {
    [void]$sb.AppendLine("  $p  |  $($entriesWithSizeRound[$p])")
}
$sb.ToString() | Set-Content $reportPath -Encoding UTF8
Write-Ok "Report saved: $reportPath"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Test output: $TestDir" -ForegroundColor Cyan
Write-Host "  - original_lcs.zip, roundtrip_lcs.zip" -ForegroundColor White
Write-Host "  - extract_original/, extract_roundtrip/" -ForegroundColor White
Write-Host "  - comparison_report.txt" -ForegroundColor White
Write-Host "========================================`n" -ForegroundColor Cyan

if (-not $KeepContainer) {
    Write-Host "Container left running. Stop with: docker compose down" -ForegroundColor Gray
}
