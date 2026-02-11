<#
.SYNOPSIS
  Creates an LCS template ZIP from an existing LCS package by removing license files
  (and optionally package payload) so it can be used as a clean skeleton for Unified→LCS conversion.

.DESCRIPTION
  Use this to turn a production/main package (e.g. AX_AIO_Main_Production_*.zip) into a
  template for DeployPortal's LcsTemplatePath. Licenses are removed from AOSService\Scripts\License.
  Optionally removes Packages\files\*.zip and Packages\*.nupkg to keep only the structure (exe, DLLs, Scripts).

.PARAMETER SourceZip
  Path to the source LCS package ZIP.

.PARAMETER OutputZip
  Path for the output template ZIP. Default: same folder as SourceZip, name with _NoLicenses suffix.

.PARAMETER RemovePackagePayload
  If set, also removes AOSService\Packages\files\*.zip and AOSService\Packages\*.nupkg so the template
  is a minimal skeleton (converter will add only converted modules). Reduces template size.

.EXAMPLE
  .\New-LcsTemplateFromPackage.ps1 -SourceZip "D:\Downloads\ф2\AX_AIO_Main_Production_2026.2.4.4 (1).zip"
  # Creates ...\AX_AIO_Main_Production_2026.2.4.4 (1)_NoLicenses.zip

.EXAMPLE
  .\New-LcsTemplateFromPackage.ps1 -SourceZip "D:\Packages\MyProduction.zip" -OutputZip "D:\DeployPortal\LcsTemplate.zip" -RemovePackagePayload
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $SourceZip,

    [Parameter(Mandatory = $false)]
    [string] $OutputZip = "",

    [Parameter(Mandatory = $false)]
    [switch] $RemovePackagePayload
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $SourceZip -PathType Leaf)) {
    Write-Error "Source ZIP not found: $SourceZip"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $dir = [System.IO.Path]::GetDirectoryName($SourceZip)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($SourceZip)
    $OutputZip = Join-Path $dir "${name}_NoLicenses.zip"
}

$tempRoot = [System.IO.Path]::GetTempPath()
$extractDir = Join-Path $tempRoot "LcsTemplateBuild_$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

try {
    Write-Host "Extracting: $SourceZip" -ForegroundColor Cyan
    Expand-Archive -LiteralPath $SourceZip -DestinationPath $extractDir -Force

    # Resolve LCS root: if zip had one top-level folder (e.g. AX_AIO_...), use it
    $entries = Get-ChildItem -Path $extractDir -Force
    $dirs = $entries | Where-Object { $_.PSIsContainer }
    $files = $entries | Where-Object { -not $_.PSIsContainer }
    $lcsRoot = $extractDir
    if ($dirs.Count -eq 1 -and $files.Count -eq 0) {
        $lcsRoot = $dirs[0].FullName
        Write-Host "LCS root folder: $($dirs[0].Name)" -ForegroundColor Gray
    }

    # AOSService path (standard LCS layout)
    $aosService = Join-Path $lcsRoot "AOSService"
    if (-not (Test-Path -LiteralPath $aosService -PathType Container)) {
        Write-Warning "AOSService not found under $lcsRoot. Checking for alternate layout..."
        # Some zips might have AOSService at top level
        $aosService = $lcsRoot
        if (-not (Test-Path (Join-Path $aosService "Packages") -PathType Container)) {
            Write-Error "AOSService/Packages structure not found. Is this a valid LCS package?"
            exit 2
        }
    }

    # 1) Remove license files: AOSService\Scripts\License\*
    $licenseDir = Join-Path (Join-Path $aosService "Scripts") "License"
    if (Test-Path -LiteralPath $licenseDir -PathType Container) {
        $licFiles = Get-ChildItem -Path $licenseDir -File -Recurse -ErrorAction SilentlyContinue
        $count = ($licFiles | Measure-Object).Count
        foreach ($f in $licFiles) {
            Remove-Item -LiteralPath $f.FullName -Force
        }
        Write-Host "Removed $count file(s) from Scripts\License" -ForegroundColor Green
    } else {
        Write-Host "No Scripts\License folder found (nothing to remove)" -ForegroundColor Gray
    }

    # 2) Optionally remove package payload so template is skeleton only (all .zip/.nupkg under AOSService\Packages)
    if ($RemovePackagePayload) {
        $packagesDir = Join-Path $aosService "Packages"
        $removed = 0
        if (Test-Path -LiteralPath $packagesDir -PathType Container) {
            Get-ChildItem -LiteralPath $packagesDir -Include "*.zip", "*.nupkg" -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Force
                $removed++
            }
        }
        Write-Host "Removed $removed package payload file(s) (skeleton-only template)" -ForegroundColor Green

        # Remove HotfixInstallationInfo.xml (lists production modules); converter will generate a new one
        $hotfixXml = Join-Path $lcsRoot "HotfixInstallationInfo.xml"
        if (Test-Path -LiteralPath $hotfixXml -PathType Leaf) {
            Remove-Item -LiteralPath $hotfixXml -Force
            Write-Host "Removed HotfixInstallationInfo.xml (converter will generate it)" -ForegroundColor Green
        }
    }

    # Create output zip from extract root (preserve single top-level folder if present)
    $zipSource = $extractDir
    if ($lcsRoot -ne $extractDir) {
        $zipSource = $lcsRoot
    }

    if (Test-Path -LiteralPath $OutputZip -PathType Leaf) {
        Remove-Item -LiteralPath $OutputZip -Force
    }
    Write-Host "Creating template ZIP: $OutputZip" -ForegroundColor Cyan
    $zipItems = Get-ChildItem -Path $zipSource -Force | ForEach-Object { $_.FullName }
    Compress-Archive -Path $zipItems -DestinationPath $OutputZip -CompressionLevel Optimal

    $len = (Get-Item -LiteralPath $OutputZip).Length / 1MB
    Write-Host "Done. Template: $OutputZip ($([math]::Round($len, 2)) MB)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next: In DeployPortal Settings set LcsTemplatePath to this file (or copy it to Resources\LcsTemplate\)." -ForegroundColor Yellow
} finally {
    if (Test-Path -LiteralPath $extractDir -PathType Container) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
