<#
.SYNOPSIS
    Publish DeployPortal as a self-contained application.
.DESCRIPTION
    Creates a publish/ folder with a fully self-contained application
    that can run on any Windows machine without installing .NET Runtime.
.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -OutputDir "C:\Deploy\DeployPortal"
#>

param(
    [string]$OutputDir = "",
    [string]$Runtime = "win-x64",
    [switch]$SingleFile
)

$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
if (-not $OutputDir) { $OutputDir = Join-Path $ProjectRoot "publish" }
$projectPath = Join-Path $ProjectRoot "src\DeployPortal\DeployPortal.csproj"

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  DeployPortal - Publish' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host ('Project:    ' + $projectPath) -ForegroundColor White
Write-Host ('Output:     ' + $OutputDir) -ForegroundColor White
Write-Host ('Runtime:    ' + $Runtime) -ForegroundColor White
Write-Host ('SingleFile: ' + $SingleFile) -ForegroundColor White
Write-Host ''

# Preserve runtime state across publish (DB + Data Protection keys).
# Deleting these causes antiforgery decrypt errors and broken Client Secret decryption.
$preserveItems = @(
    'deploy-portal.db',
    'deploy-portal.db-shm',
    'deploy-portal.db-wal',
    'DataProtection-Keys'
)
$preserveRoot = Join-Path $env:TEMP ("DeployPortal_publish_preserve_" + [guid]::NewGuid().ToString('N'))
$preserved = @()
if (Test-Path $OutputDir) {
    New-Item -ItemType Directory -Force -Path $preserveRoot | Out-Null
    foreach ($name in $preserveItems) {
        $src = Join-Path $OutputDir $name
        if (Test-Path $src) {
            $dest = Join-Path $preserveRoot $name
            Copy-Item $src $dest -Recurse -Force
            $preserved += $name
            Write-Host ('Preserving: ' + $name) -ForegroundColor Yellow
        }
    }
    Write-Host 'Cleaning previous publish...' -ForegroundColor Yellow
    Remove-Item $OutputDir -Recurse -Force
}

# Build arguments
$buildArgs = @(
    "publish"
    $projectPath
    "--configuration", "Release"
    "--runtime", $Runtime
    "--self-contained", "true"
    "--output", $OutputDir
)

if ($SingleFile) {
    $buildArgs += "-p:PublishSingleFile=true"
    $buildArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}

# Trim unused assemblies to reduce size
$buildArgs += "-p:PublishTrimmed=false"

Write-Host ('Running: dotnet ' + ($buildArgs -join ' ')) -ForegroundColor Cyan
Write-Host ''

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'PUBLISH FAILED!' -ForegroundColor Red
    exit 1
}

# Restore preserved DB / Data Protection keys into the new publish output
if ($preserved.Count -gt 0 -and (Test-Path $preserveRoot)) {
    foreach ($name in $preserved) {
        $src = Join-Path $preserveRoot $name
        $dest = Join-Path $OutputDir $name
        if (Test-Path $src) {
            if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
            Copy-Item $src $dest -Recurse -Force
            Write-Host ('Restored: ' + $name) -ForegroundColor Green
        }
    }
    Remove-Item $preserveRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# Copy helper files (including prerequisites script so it can be run from publish folder)
$helperFiles = @(
    @{ Dir = "scripts"; File = "Setup-ServicePrincipal.ps1" },
    @{ Dir = "documents"; File = "Setup-ServicePrincipal-Manual.md" },
    @{ Dir = "scripts"; File = "check-prerequisites.ps1" }
)

foreach ($item in $helperFiles) {
    $src = Join-Path $ProjectRoot (Join-Path $item.Dir $item.File)
    if (Test-Path $src) {
        Copy-Item $src $OutputDir
        Write-Host ('Copied: ' + $item.File) -ForegroundColor Green
    }
}

# Create a launch script (single-quoted here-string so @ is literal)
$startCmd = @'
@echo off
echo Starting DeployPortal...
echo.
echo Open in browser: http://localhost:5000
echo Press Ctrl+C to stop.
echo.
start http://localhost:5000
DeployPortal.exe --urls "http://localhost:5000"
'@
$startCmd | Out-File (Join-Path $OutputDir "start.cmd") -Encoding ASCII

# Copy README for distribution
$readmeSrc = Join-Path $ProjectRoot "documents\publish-README.md"
if (Test-Path $readmeSrc) {
    Copy-Item $readmeSrc (Join-Path $OutputDir "README.md")
    Write-Host 'Copied: README.md' -ForegroundColor Green
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host '  PUBLISH COMPLETE!' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host ''
Write-Host ('Output directory: ' + $OutputDir) -ForegroundColor White

$exePath = Join-Path $OutputDir 'DeployPortal.exe'
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host ('Executable size: ' + [math]::Round($size, 1) + ' MB') -ForegroundColor White
}

$totalSize = (Get-ChildItem $OutputDir -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ('Total folder size: ' + [math]::Round($totalSize, 1) + ' MB') -ForegroundColor White
Write-Host ''
Write-Host 'To distribute:' -ForegroundColor Cyan
Write-Host ('  1. Copy the ' + $OutputDir + ' folder to the target machine') -ForegroundColor White
Write-Host '  2. Run start.cmd or DeployPortal.exe' -ForegroundColor White
Write-Host '  3. Open Settings page and configure tool paths' -ForegroundColor White
Write-Host ''
