<#
.SYNOPSIS
  Upload a package to Azure Artifacts (Universal Package) from command line.
  Use this to test manually: you will see any "please log in" or other prompts.
  Edit the variables below and run in PowerShell.

  Full command for CMD (replace values; for .zip use path to EXTRACTED folder):
    "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd" artifacts universal publish --organization https://dev.azure.com/YOUR_ORG --project "YOUR_PROJECT" --scope project --feed Packages --name PackageName --version 1.0.0 --path "C:\path\to\folder"

.EXAMPLE
  .\upload-universal-package.ps1
#>

# ========== EDIT THESE ==========
$Organization = "YOUR_ORG"           # e.g. sisn or myorg
$Project      = "YOUR_PROJECT"       # e.g. "SIS D365FO Products"
$Feed         = "Packages"
$PackageName  = "AXDeployablePackagePCM_202"
$Version      = "1.0.1770918742"
$PackagePath  = "C:\path\to\your\package.zip"   # .zip file or folder to publish
# ================================

$ErrorActionPreference = "Stop"
$orgUrl = "https://dev.azure.com/$Organization"

# Resolve az path (same logic as DeployPortal)
function Find-AzPath {
    if ($env:PATH) {
        foreach ($dir in $env:PATH.Split([System.IO.Path]::PathSeparator, [StringSplitOptions]::RemoveEmptyEntries)) {
            $dir = $dir.Trim()
            if (-not $dir) { continue }
            foreach ($ext in @("az.cmd", "az.exe", "az")) {
                $full = Join-Path $dir $ext
                if (Test-Path -LiteralPath $full -PathType Leaf) { return $full }
            }
        }
    }
    $defaultWin = Join-Path $env:ProgramFiles "Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
    if (Test-Path -LiteralPath $defaultWin -PathType Leaf) { return $defaultWin }
    return $null
}

$azPath = Find-AzPath
if (-not $azPath) {
    Write-Error "Azure CLI (az) not found. Install from https://learn.microsoft.com/cli/azure/install-azure-cli"
    exit 1
}

if (-not (Test-Path -LiteralPath $PackagePath)) {
    Write-Error "Package path does not exist: $PackagePath"
    exit 1
}

$pathToPublish = $PackagePath
$tempDir = $null

if ($PackagePath -match '\.zip$') {
    $tempDir = Join-Path $env:TEMP "DeployPortal_Upack_$(New-Guid | ForEach-Object { $_.Guid.Replace('-','') })"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Write-Host "Extracting zip to $tempDir ..."
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $tempDir -Force
    $pathToPublish = $tempDir
}

try {
    $args = @(
        "artifacts", "universal", "publish",
        "--organization", $orgUrl,
        "--project", $Project,
        "--scope", "project",
        "--feed", $Feed,
        "--name", $PackageName,
        "--version", $Version,
        "--path", $pathToPublish
    )

    $cmdLine = "`"$azPath`" " + ($args -join ' ')
    Write-Host "Full command (you can run this in cmd):" -ForegroundColor Cyan
    Write-Host $cmdLine
    Write-Host ""
    Write-Host "Running in interactive mode (you will see login prompts if any)..." -ForegroundColor Yellow
    Write-Host ""

    & $azPath @args
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Host "Exit code: $exitCode" -ForegroundColor Red
        exit $exitCode
    }
    Write-Host "Done." -ForegroundColor Green
}
finally {
    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
