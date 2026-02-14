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
    [string]$OutputDir = (Join-Path $PSScriptRoot "publish"),
    [string]$Runtime = "win-x64",
    [switch]$SingleFile
)

$projectPath = Join-Path $PSScriptRoot "src\DeployPortal\DeployPortal.csproj"

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  DeployPortal - Publish' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host ('Project:    ' + $projectPath) -ForegroundColor White
Write-Host ('Output:     ' + $OutputDir) -ForegroundColor White
Write-Host ('Runtime:    ' + $Runtime) -ForegroundColor White
Write-Host ('SingleFile: ' + $SingleFile) -ForegroundColor White
Write-Host ''

# Clean previous output
if (Test-Path $OutputDir) {
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

# Copy helper files (including prerequisites script so it can be run from publish folder)
$helperFiles = @(
    "Setup-ServicePrincipal.ps1",
    "documents/Setup-ServicePrincipal-Manual.md",
    "check-prerequisites.ps1"
)

foreach ($file in $helperFiles) {
    $src = Join-Path $PSScriptRoot $file
    if (Test-Path $src) {
        Copy-Item $src $OutputDir
        Write-Host ('Copied: ' + $file) -ForegroundColor Green
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
$readmeSrc = Join-Path $PSScriptRoot "documents/publish-README.md"
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
