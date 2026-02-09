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

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  DeployPortal — Publish" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Project:    $projectPath" -ForegroundColor White
Write-Host "Output:     $OutputDir" -ForegroundColor White
Write-Host "Runtime:    $Runtime" -ForegroundColor White
Write-Host "SingleFile: $SingleFile" -ForegroundColor White
Write-Host ""

# Clean previous output
if (Test-Path $OutputDir) {
    Write-Host "Cleaning previous publish..." -ForegroundColor Yellow
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

Write-Host "Running: dotnet $($buildArgs -join ' ')" -ForegroundColor Cyan
Write-Host ""

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "PUBLISH FAILED!" -ForegroundColor Red
    exit 1
}

# Copy helper files
$helperFiles = @(
    "Setup-ServicePrincipal.ps1",
    "Setup-ServicePrincipal-Manual.md"
)

foreach ($file in $helperFiles) {
    $src = Join-Path $PSScriptRoot $file
    if (Test-Path $src) {
        Copy-Item $src $OutputDir
        Write-Host "Copied: $file" -ForegroundColor Green
    }
}

# Create a launch script
@"
@echo off
echo Starting DeployPortal...
echo.
echo Open in browser: http://localhost:5000
echo Press Ctrl+C to stop.
echo.
start http://localhost:5000
DeployPortal.exe --urls "http://localhost:5000"
"@ | Out-File (Join-Path $OutputDir "start.cmd") -Encoding ASCII

# Create README for distribution
@"
# DeployPortal — D365FO Package Deployment Tool

## Quick Start

1. Run ``start.cmd`` — opens browser automatically
2. Go to **Settings** — configure paths to ModelUtil.exe and PAC CLI
3. Go to **Environments** — add target Power Platform environments
4. Go to **Packages** — upload packages (LCS, Unified, or Other)
5. Go to **Deploy** — select package + environments and deploy

## Prerequisites on Target Machine

| Component       | Required? | How to install                                           |
|-----------------|-----------|----------------------------------------------------------|
| .NET Runtime    | No        | Bundled (self-contained publish)                         |
| ModelUtil.exe   | For LCS conversion | Installed with D365FO dev tools              |
| PAC CLI         | For deployment | ``dotnet tool install --global Microsoft.PowerApps.CLI.Tool`` |
| Azure SP        | For deployment | Run ``Setup-ServicePrincipal.ps1`` or follow Manual.md |

## Configuration

- All settings are configurable from the **Settings** page in the UI
- Settings are saved to ``usersettings.json`` in the app directory
- Database (``deploy-portal.db``) is created automatically in the app directory
- Package storage defaults to ``Packages/`` subdirectory

## Ports

Default: http://localhost:5000
To change: ``DeployPortal.exe --urls "http://localhost:8080"``
To allow remote access: ``DeployPortal.exe --urls "http://0.0.0.0:5000"``
"@ | Out-File (Join-Path $OutputDir "README.md") -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  PUBLISH COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory: $OutputDir" -ForegroundColor White

$exePath = Join-Path $OutputDir "DeployPortal.exe"
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host "Executable size: $([math]::Round($size, 1)) MB" -ForegroundColor White
}

$totalSize = (Get-ChildItem $OutputDir -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host "Total folder size: $([math]::Round($totalSize, 1)) MB" -ForegroundColor White
Write-Host ""
Write-Host "To distribute:" -ForegroundColor Cyan
Write-Host "  1. Copy the '$OutputDir' folder to the target machine" -ForegroundColor White
Write-Host "  2. Run 'start.cmd' or 'DeployPortal.exe'" -ForegroundColor White
Write-Host "  3. Open Settings page and configure tool paths" -ForegroundColor White
Write-Host ""
