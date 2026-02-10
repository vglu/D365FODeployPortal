<#
.SYNOPSIS
    Диагностика и тест конвертации реального LCS-пакета (например AX_AIO_Main_Production).
    Показывает структуру ZIP и результат встроенного конвертера.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$PackagePath = "D:\Downloads\AX_AIO_Main_Production_2026.2.4.4.zip"
)

$ErrorActionPreference = "Stop"

Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host "  ДИАГНОСТИКА И ТЕСТ КОНВЕРТАЦИИ LCS -> UNIFIED" -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $PackagePath)) {
    Write-Host "Файл не найден: $PackagePath" -ForegroundColor Red
    Write-Host "Укажите путь к ZIP: .\test-convert-real-package.ps1 -PackagePath 'C:\path\to\package.zip'" -ForegroundColor Yellow
    exit 1
}

$fullPath = (Resolve-Path $PackagePath).Path
$zipSize = (Get-Item $fullPath).Length
Write-Host "Пакет: $fullPath" -ForegroundColor White
Write-Host "Размер: $([math]::Round($zipSize/1KB, 1)) KB" -ForegroundColor White
Write-Host ""

# ===== 1. Содержимое ZIP =====
Write-Host "--- Содержимое ZIP (корень и AOSService) ---" -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($fullPath)
$entries = $zip.Entries | ForEach-Object { $_.FullName } | Sort-Object
$zip.Dispose()

$rootEntries = $entries | Where-Object { $_ -notmatch "/" -and $_ -notmatch "\\" }
$rootEntries | ForEach-Object { Write-Host "  /$_" -ForegroundColor Gray }

$aosEntries = $entries | Where-Object { $_ -match "^AOSService" }
if ($aosEntries.Count -gt 0) {
    Write-Host ""
    Write-Host "  AOSService (первые 50 записей):" -ForegroundColor Gray
    $aosEntries | Select-Object -First 50 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    if ($aosEntries.Count -gt 50) {
        Write-Host "    ... и ещё $($aosEntries.Count - 50) записей" -ForegroundColor DarkGray
    }
    $filesDir = $aosEntries | Where-Object { $_ -match "AOSService[/\\]Packages[/\\]files[/\\]" }
    $packagesDir = $aosEntries | Where-Object { $_ -match "AOSService[/\\]Packages[/\\]" } | Where-Object { $_ -notmatch "[/\\]files[/\\]" }
    Write-Host ""
    Write-Host "  Записей в AOSService/Packages/files/: $($filesDir.Count)" -ForegroundColor $(if ($filesDir.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  Записей в AOSService/Packages/ (не files): $($packagesDir.Count)" -ForegroundColor $(if ($packagesDir.Count -gt 0) { "Green" } else { "Gray" })
    $zipNames = $entries | Where-Object { $_ -match "\.(zip|nupkg)$" } | ForEach-Object { Split-Path $_ -Leaf }
    $dynamicsaxZips = $zipNames | Where-Object { $_ -like "dynamicsax-*" }
    $nupkgs = $zipNames | Where-Object { $_ -like "*.nupkg" }
    Write-Host "  Файлов dynamicsax-*.zip: $($dynamicsaxZips.Count)" -ForegroundColor $(if ($dynamicsaxZips.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  Файлов *.nupkg: $($nupkgs.Count)" -ForegroundColor $(if ($nupkgs.Count -gt 0) { "Green" } else { "Gray" })
    if ($dynamicsaxZips.Count -gt 0) { $dynamicsaxZips | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
    if ($nupkgs.Count -gt 0 -and $dynamicsaxZips.Count -eq 0) { $nupkgs | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
} else {
    Write-Host "  Папки AOSService в ZIP не найдено." -ForegroundColor Red
}

Write-Host ""

# ===== 2. Запуск теста конвертации =====
Write-Host "--- Запуск встроенного конвертера (через тест) ---" -ForegroundColor Yellow
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
    Write-Host "  Тест конвертации завершён. См. вывод выше: число модулей и размер Unified." -ForegroundColor Green
} else {
    Write-Host "  Test failed or converter produced 0 modules (result ~51 KB, template only)." -ForegroundColor Yellow
    Write-Host "  If package has only *.nupkg (no dynamicsax-*.zip in files/), converter now supports nupkg." -ForegroundColor Yellow
}
Write-Host ("=" * 70) -ForegroundColor Cyan

exit $LASTEXITCODE
