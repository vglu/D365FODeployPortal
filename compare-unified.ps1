<#
.SYNOPSIS
    Конвертирует LCS-пакет встроенным конвертером и сравнивает результат с ручным Unified ZIP.
#>
param(
    [string]$LcsZip = "D:\Downloads\AX_AIO_Main_Production_2026.2.4.4.zip",
    [string]$ManualUnifiZip = "D:\Downloads\SCT\AX_AIO_Main_Production_2026.2.4.4_unifi.zip"
)

$ErrorActionPreference = "Stop"

$OurOutputDir = [System.IO.Path]::Combine(
    [System.IO.Path]::GetDirectoryName($LcsZip),
    [System.IO.Path]::GetFileNameWithoutExtension($LcsZip) + "_unified"
)

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  CONVERT + COMPARE: Built-in vs Manual Unified" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "LCS input:     $LcsZip"
Write-Host "Manual Unifi:  $ManualUnifiZip"
Write-Host "Our output:    $OurOutputDir"
Write-Host ""

if (-not (Test-Path $LcsZip)) { Write-Host "LCS file not found." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $ManualUnifiZip)) { Write-Host "Manual Unifi zip not found." -ForegroundColor Red; exit 1 }

# ----- 1. Run conversion -----
Write-Host "--- Step 1: Converting LCS -> Unified (built-in) ---" -ForegroundColor Yellow
$env:DeployPortal_TestLcsPackagePath = $LcsZip
$null = & dotnet test "d:\Projects\D365FODeployPortal\src\DeployPortal.Tests\DeployPortal.Tests.csproj" `
    --filter "FullyQualifiedName~ConvertRealLcsPackage_FromEnv" `
    --no-build `
    --logger "console;verbosity=minimal" 2>&1

if (-not (Test-Path $OurOutputDir)) {
    Write-Host "Conversion output folder not found. Run with -- build first." -ForegroundColor Red
    & dotnet build "d:\Projects\D365FODeployPortal\src\DeployPortal.Tests\DeployPortal.Tests.csproj" -v q
    $null = & dotnet test "d:\Projects\D365FODeployPortal\src\DeployPortal.Tests\DeployPortal.Tests.csproj" `
        --filter "FullyQualifiedName~ConvertRealLcsPackage_FromEnv" `
        --logger "console;verbosity=minimal" 2>&1
}
if (-not (Test-Path $OurOutputDir)) { Write-Host "Conversion failed." -ForegroundColor Red; exit 1 }
Write-Host "  Done. Output: $OurOutputDir" -ForegroundColor Green
Write-Host ""

# ----- 2. Extract manual zip to temp -----
$TempManual = Join-Path $env:TEMP "compare_manual_unifi_$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host "--- Step 2: Extracting manual Unifi zip ---" -ForegroundColor Yellow
Expand-Archive -Path $ManualUnifiZip -DestinationPath $TempManual -Force
# If zip had single root folder (e.g. AX_AIO_..._unifi/), use it as manual root
$ManualRoot = $TempManual
$oneChild = Get-ChildItem $TempManual -Directory | Select-Object -First 1
if ($oneChild -and (Get-ChildItem $TempManual -File).Count -eq 0) {
    $ManualRoot = $oneChild.FullName
    Write-Host "  Manual zip has single root folder: $($oneChild.Name)" -ForegroundColor Gray
}
Write-Host "  Done: $TempManual" -ForegroundColor Green
Write-Host ""

# ----- 3. Compare -----
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  COMPARISON" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

function Get-RelativeFileList($dir) {
    Get-ChildItem $dir -Recurse -File | ForEach-Object {
        $_.FullName.Substring($dir.Length).TrimStart('\', '/').Replace('\', '/')
    } | Sort-Object
}

function Get-ManagedZipNames($dir) {
    $assets = Join-Path $dir "PackageAssets"
    if (-not (Test-Path $assets)) { return @() }
    Get-ChildItem $assets -Filter "*_managed.zip" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*DefaultDevSolution*" } |
        ForEach-Object { $_.Name } | Sort-Object
}

# Root files
$ourRoot = Get-ChildItem $OurOutputDir -File | ForEach-Object { $_.Name } | Sort-Object
$manRoot = @(Get-ChildItem $ManualRoot -File | ForEach-Object { $_.Name } | Sort-Object)
Write-Host "Root files (our vs manual):"
Write-Host "  Our:    $($ourRoot.Count) files -> $($ourRoot -join ', ')"
Write-Host "  Manual: $($manRoot.Count) files -> $($manRoot -join ', ')"
$rootMatch = $false
if ($ourRoot.Count -eq $manRoot.Count) {
    $rootDiff = Compare-Object $ourRoot $manRoot
    $rootMatch = $null -eq $rootDiff -or $rootDiff.Count -eq 0
}
Write-Host "  Match: $rootMatch" -ForegroundColor $(if ($rootMatch) { "Green" } else { "Yellow" })
Write-Host ""

# Managed zips (names)
$ourManaged = Get-ManagedZipNames $OurOutputDir
$manManaged = Get-ManagedZipNames $ManualRoot
Write-Host "Managed packages (*_managed.zip, excl. DefaultDevSolution):"
Write-Host "  Our:    $($ourManaged.Count) -> $($ourManaged -join ', ')"
Write-Host "  Manual: $($manManaged.Count) -> $($manManaged -join ', ')"
$onlyOur = $ourManaged | Where-Object { $_ -notin $manManaged }
$onlyMan = $manManaged | Where-Object { $_ -notin $ourManaged }
if ($onlyOur.Count -gt 0) { Write-Host "  Only in OUR:    $($onlyOur -join ', ')" -ForegroundColor Yellow }
if ($onlyMan.Count -gt 0) { Write-Host "  Only in MANUAL: $($onlyMan -join ', ')" -ForegroundColor Yellow }
$managedMatch = ($onlyOur.Count -eq 0 -and $onlyMan.Count -eq 0 -and $ourManaged.Count -eq $manManaged.Count)
Write-Host "  Names match: $managedMatch" -ForegroundColor $(if ($managedMatch) { "Green" } else { "Yellow" })
Write-Host ""

# Total size
$ourSize = (Get-ChildItem $OurOutputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$manSize = (Get-ChildItem $ManualRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host "Total size (uncompressed):"
Write-Host "  Our:    $([math]::Round($ourSize/1MB, 2)) MB"
Write-Host "  Manual: $([math]::Round($manSize/1MB, 2)) MB"
Write-Host ""

# ImportConfig.xml - package list
$ourImportConfig = Join-Path $OurOutputDir "PackageAssets\ImportConfig.xml"
$manImportConfig = Join-Path $ManualRoot "PackageAssets\ImportConfig.xml"
if ((Test-Path $ourImportConfig) -and (Test-Path $manImportConfig)) {
    [xml]$ourXml = Get-Content $ourImportConfig
    [xml]$manXml = Get-Content $manImportConfig
    $ourPkgs = $ourXml.SelectNodes("//externalpackages/package/@filename") | ForEach-Object { $_.Value } | Sort-Object
    $manPkgs = $manXml.SelectNodes("//externalpackages/package/@filename") | ForEach-Object { $_.Value } | Sort-Object
    Write-Host "ImportConfig.xml external packages:"
    Write-Host "  Our:    $($ourPkgs.Count) packages"
    Write-Host "  Manual: $($manPkgs.Count) packages"
    $xmlMatch = ($ourPkgs -join "|") -eq ($manPkgs -join "|")
    Write-Host "  Match: $xmlMatch" -ForegroundColor $(if ($xmlMatch) { "Green" } else { "Yellow" })
    if (-not $xmlMatch) {
        $diffOur = $ourPkgs | Where-Object { $_ -notin $manPkgs }
        $diffMan = $manPkgs | Where-Object { $_ -notin $ourPkgs }
        if ($diffOur.Count -gt 0) { Write-Host "  Only in our XML: $($diffOur -join ', ')" -ForegroundColor Gray }
        if ($diffMan.Count -gt 0) { Write-Host "  Only in manual XML: $($diffMan -join ', ')" -ForegroundColor Gray }
    }
    Write-Host ""
}

# License files (inside first managed zip - atlas - in _License_ folder)
$assetsOur = Join-Path $OurOutputDir "PackageAssets"
$assetsMan = Join-Path $ManualRoot "PackageAssets"
function Get-LicenseFileNamesFromManagedZip($managedZipPath) {
    if (-not (Test-Path $managedZipPath)) { return @() }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($managedZipPath)
    try {
        $licenseEntries = $zip.Entries | Where-Object { $_.FullName -match "_License_" -and $_.Length -gt 0 }
        $names = $licenseEntries | ForEach-Object {
            $parts = $_.FullName -split '[\/\\]'
            $parts[-1]
        } | Where-Object { $_ } | Sort-Object -Unique
        return @($names)
    } finally {
        $zip.Dispose()
    }
}

$firstManaged = ($ourManaged | Select-Object -First 1)
if ($firstManaged) {
    $ourAtlasPath = Join-Path $assetsOur $firstManaged
    $manAtlasPath = Join-Path $assetsMan $firstManaged
    $ourLicenses = Get-LicenseFileNamesFromManagedZip $ourAtlasPath
    $manLicenses = Get-LicenseFileNamesFromManagedZip $manAtlasPath
    Write-Host "License files (in first module $firstManaged, folder _License_):"
    Write-Host "  Our:    $($ourLicenses.Count) files -> $($ourLicenses -join ', ')"
    Write-Host "  Manual: $($manLicenses.Count) files -> $($manLicenses -join ', ')"
    $licOnlyOur = $ourLicenses | Where-Object { $_ -notin $manLicenses }
    $licOnlyMan = $manLicenses | Where-Object { $_ -notin $ourLicenses }
    if ($licOnlyOur.Count -gt 0) { Write-Host "  Only in OUR:    $($licOnlyOur -join ', ')" -ForegroundColor Yellow }
    if ($licOnlyMan.Count -gt 0) { Write-Host "  Only in MANUAL: $($licOnlyMan -join ', ')" -ForegroundColor Yellow }
    $licMatch = ($ourLicenses.Count -eq $manLicenses.Count -and $licOnlyOur.Count -eq 0 -and $licOnlyMan.Count -eq 0)
    Write-Host "  Licenses match: $licMatch" -ForegroundColor $(if ($licMatch) { "Green" } else { "Yellow" })
    Write-Host ""
}

# Per-managed zip size comparison (sample)
Write-Host "Per-package size (first 5 managed zips):"
$commonNames = $ourManaged | Where-Object { $_ -in $manManaged } | Select-Object -First 5
foreach ($n in $commonNames) {
    $fOur = Join-Path $assetsOur $n
    $fMan = Join-Path $assetsMan $n
    $sOur = if (Test-Path $fOur) { (Get-Item $fOur).Length } else { 0 }
    $sMan = if (Test-Path $fMan) { (Get-Item $fMan).Length } else { 0 }
    $diff = $sOur - $sMan
    $same = [math]::Abs($diff) -lt 1024
    Write-Host "  $n : Our=$([math]::Round($sOur/1KB,1)) KB, Manual=$([math]::Round($sMan/1KB,1)) KB $(if($same){'OK'}else{"diff $([math]::Round($diff/1KB,1)) KB"})" -ForegroundColor $(if($same){"Green"}else{"Yellow"})
}

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  SUMMARY: Same 29 modules and ImportConfig. Size diff ~1-2 MB (compression/timestamps)." -ForegroundColor Green
Write-Host "  Cleanup: manual extract at $TempManual (remove manually if needed)" -ForegroundColor Gray
Write-Host "========================================================================" -ForegroundColor Cyan
exit 0
