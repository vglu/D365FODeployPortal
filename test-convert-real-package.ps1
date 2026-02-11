<#
.SYNOPSIS
    Diagnostics and conversion test for a real LCS package (e.g. AX_AIO_Main_Production).
    Shows ZIP structure and built-in converter result.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$PackagePath = "D:\Downloads\AX_AIO_Main_Production_2026.2.4.4.zip"
)

$ErrorActionPreference = "Stop"

Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host "  LCS -> UNIFIED CONVERSION DIAGNOSTICS" -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $PackagePath)) {
    Write-Host "File not found: $PackagePath" -ForegroundColor Red
    Write-Host "Specify path: .\test-convert-real-package.ps1 -PackagePath 'C:\path\to\package.zip'" -ForegroundColor Yellow
    exit 1
}

$fullPath = (Resolve-Path $PackagePath).Path
$zipSize = (Get-Item $fullPath).Length
Write-Host "Package: $fullPath" -ForegroundColor White
Write-Host "Size: $([math]::Round($zipSize/1KB, 1)) KB" -ForegroundColor White
Write-Host ""

# ===== 1. ZIP contents =====
Write-Host "--- ZIP contents (root and AOSService) ---" -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($fullPath)
$entries = $zip.Entries | ForEach-Object { $_.FullName } | Sort-Object
$zip.Dispose()

$rootEntries = $entries | Where-Object { $_ -notmatch "/" -and $_ -notmatch "\\" }
$rootEntries | ForEach-Object { Write-Host "  /$_" -ForegroundColor Gray }

$aosEntries = $entries | Where-Object { $_ -match "^AOSService" }
if ($aosEntries.Count -gt 0) {
    Write-Host ""
    Write-Host "  AOSService (first 50 entries):" -ForegroundColor Gray
    $aosEntries | Select-Object -First 50 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    if ($aosEntries.Count -gt 50) {
        Write-Host "    ... and $($aosEntries.Count - 50) more" -ForegroundColor DarkGray
    }
    $filesDir = $aosEntries | Where-Object { $_ -match "AOSService[/\\]Packages[/\\]files[/\\]" }
    $packagesDir = $aosEntries | Where-Object { $_ -match "AOSService[/\\]Packages[/\\]" } | Where-Object { $_ -notmatch "[/\\]files[/\\]" }
    Write-Host ""
    Write-Host "  Entries in AOSService/Packages/files/: $($filesDir.Count)" -ForegroundColor $(if ($filesDir.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  Entries in AOSService/Packages/ (not files): $($packagesDir.Count)" -ForegroundColor $(if ($packagesDir.Count -gt 0) { "Green" } else { "Gray" })
    $zipNames = $entries | Where-Object { $_ -match "\.(zip|nupkg)$" } | ForEach-Object { Split-Path $_ -Leaf }
    $dynamicsaxZips = $zipNames | Where-Object { $_ -like "dynamicsax-*" }
    $nupkgs = $zipNames | Where-Object { $_ -like "*.nupkg" }
    Write-Host "  dynamicsax-*.zip files: $($dynamicsaxZips.Count)" -ForegroundColor $(if ($dynamicsaxZips.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  *.nupkg files: $($nupkgs.Count)" -ForegroundColor $(if ($nupkgs.Count -gt 0) { "Green" } else { "Gray" })
    if ($dynamicsaxZips.Count -gt 0) { $dynamicsaxZips | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
    if ($nupkgs.Count -gt 0 -and $dynamicsaxZips.Count -eq 0) { $nupkgs | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
} else {
    Write-Host "  AOSService folder not found in ZIP." -ForegroundColor Red
}

Write-Host ""

# ===== 2. Run conversion test =====
Write-Host "--- Running built-in converter (via test) ---" -ForegroundColor Yellow
$env:DeployPortal_TestLcsPackagePath = $fullPath
$testResult = & dotnet test "d:\Projects\D365FODeployPortal\src\DeployPortal.Tests\DeployPortal.Tests.csproj" `
    --filter "FullyQualifiedName~ConvertRealLcsPackage_FromEnv" `
    --no-build `
    --logger "console;verbosity=detailed" 2>&1

# Try build then run if no-build failed
if ($LASTEXITCODE -ne 0) {
    $testResult = & dotnet test "d:\Projects\D365FODeployPortal\src\DeployPortal.Tests\DeployPortal.Tests.csproj" `
        --filter "FullyQualifiedName~ConvertRealLcsPackage_FromEnv" `
        --logger "console;verbosity=detailed" 2>&1
}

$testResult | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
if ($LASTEXITCODE -eq 0) {
    Write-Host "  Conversion test done. See output above: module count and Unified size." -ForegroundColor Green
} else {
    Write-Host "  Test failed or converter produced 0 modules (result ~51 KB, template only)." -ForegroundColor Yellow
    Write-Host "  If package has only *.nupkg (no dynamicsax-*.zip in files/), converter now supports nupkg." -ForegroundColor Yellow
}
Write-Host ("=" * 70) -ForegroundColor Cyan

exit $LASTEXITCODE
