<#
.SYNOPSIS
  Builds the exact "az artifacts universal publish" command using the same parameters
  as the Deploy Portal: reads org/project/feed from usersettings.json and optionally
  package path/name from the portal database (deploy-portal.db).
  Use this to run the upload from command line and see login prompts or errors.

.PARAMETER PackageName
  If not specified and DB is available, uses the latest package name from the database.
  Example: MyDeployablePackage_1.0.0.0

.PARAMETER Version
  Version for the universal package. Default: 1.0.<UnixTimestamp>

.PARAMETER NoRun
  Only print the full command and paths; do not run az.

.PARAMETER Pat
  Azure DevOps PAT (scope: Packaging read/write, Release read/write). Same as in Deploy Portal. When set, az uses it via AZURE_DEVOPS_EXT_PAT and no 'az login' is needed.

.PARAMETER DefinitionId
  Release definition ID (e.g. from Azure DevOps Pipelines -> Releases -> definition). If set together with ArtifactAlias and Pat, after upload the script creates a release.

.PARAMETER ArtifactAlias
  Artifact alias in the release definition (e.g. _universal-package). Must match the alias of the Universal Package artifact.

.PARAMETER AdditionalArtifactVersions
  Hashtable of alias -> version/id for other artifacts that use "Specify at the time of release creation". E.g. for a Build artifact: @{ "_build-pipeline" = "12345" } (build ID). If omitted, the script will try to use the latest build for Build-type artifacts.

.EXAMPLE
  .\upload-universal-package-from-portal.ps1
  .\upload-universal-package-from-portal.ps1 -NoRun
  .\upload-universal-package-from-portal.ps1 -Pat "your-pat-token"
  .\upload-universal-package-from-portal.ps1 -Feed PPackages -Pat "..."   # when feed "Packages" doesn't exist
  .\upload-universal-package-from-portal.ps1 -Feed PPackages -Pat "..." -DefinitionId 28 -ArtifactAlias "_universal-package"   # upload and start release
  .\upload-universal-package-from-portal.ps1 -DefinitionId 28 -ArtifactAlias "_universal-package" -Pat "..." -AdditionalArtifactVersions @{ "_build-pipeline" = "12345" }   # if release has Build artifact with "specify at creation"
#>

param(
    [string] $PackageName = "",
    [string] $Version      = "",
    [string] $PackagePath  = "",   # if set, overrides portal/DB
    [string] $Feed         = "",   # e.g. PPackages; if set, overrides portal/settings (use when feed "Packages" doesn't exist)
    [string] $PortalBaseUrl = "",  # e.g. http://localhost:5137; empty = try 5137, 5000, 5050
    [string] $Pat          = "",   # Azure DevOps PAT (Packaging + Release). Avoids az login; required for release creation.
    [int]    $DefinitionId = 0,   # Release definition ID; if set with ArtifactAlias, create release after upload
    [string] $ArtifactAlias = "",  # Artifact alias (e.g. _universal-package); required when DefinitionId is set
    [hashtable] $AdditionalArtifactVersions = @{},  # e.g. @{ "_build-pipeline" = "12345" } for Build artifact build ID
    [switch] $NoRun
)

$ErrorActionPreference = "Stop"
$FeedOverride = $Feed   # param -Feed overrides portal/settings (e.g. use PPackages when "Packages" doesn't exist)

# ---------- Try to get params from running Deploy Portal first ----------
$Organization = $null
$Project = $null
$Feed = $null
$fromPortal = $false
$baseUrls = @("http://localhost:5137", "http://localhost:5000", "http://localhost:5050")
if ($PortalBaseUrl) { $baseUrls = @($PortalBaseUrl.TrimEnd('/')) }
foreach ($base in $baseUrls) {
    try {
        $uri = "$base/api/release-params"
        $resp = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 3 -ErrorAction Stop
        if ($resp.org -and $resp.project) {
            $Organization = $resp.org
            $Project = $resp.project
            $Feed = if ($resp.feed) { $resp.feed } else { "PPackages" }
            if ($resp.packagePath -and (Test-Path -LiteralPath $resp.packagePath -PathType Leaf)) {
                $PackagePath = $resp.packagePath
                if ($resp.packageName) { $PackageName = $resp.packageName }
                if ($resp.version) { $Version = $resp.version }
                $fromPortal = $true
                Write-Host "Using parameters from running Deploy Portal: $base" -ForegroundColor Green
                break
            }
            if ($resp.packageName) { $PackageName = $resp.packageName }
            if ($resp.version) { $Version = $resp.version }
            $fromPortal = $true
            break
        }
    } catch {
        # Portal not running or wrong port, try next
    }
}

# ---------- If not from portal, read usersettings.json ----------
if (-not $fromPortal -or -not $Organization) {
    $userSettingsPath = if ($env:LOCALAPPDATA) {
        Join-Path $env:LOCALAPPDATA "DeployPortal\usersettings.json"
    } else {
        Join-Path $env:USERPROFILE ".local\DeployPortal\usersettings.json"
    }
    if (-not (Test-Path -LiteralPath $userSettingsPath -PathType Leaf)) {
        Write-Host "Deploy Portal not running and user settings not found: $userSettingsPath" -ForegroundColor Red
        Write-Host "Start the Deploy Portal or run it once to save settings."
        exit 1
    }
    $userSettingsJson = Get-Content -LiteralPath $userSettingsPath -Raw
    $userSettings = $userSettingsJson | ConvertFrom-Json
    if (-not $Organization) {
        $Organization = $userSettings.AzureDevOpsOrganization
        $Project      = $userSettings.AzureDevOpsProject
        $Feed         = if ($userSettings.ReleasePipelineFeedName) { $userSettings.ReleasePipelineFeedName } else { "PPackages" }
    }
    if (-not $Organization -or -not $Project) {
        Write-Host "AzureDevOpsOrganization or AzureDevOpsProject missing. Set them in Deploy Portal Settings." -ForegroundColor Red
        exit 1
    }
} else {
    $userSettings = @{ PackageStoragePath = ""; DatabasePath = "" }
}
# Ensure we have userSettings from file for DatabasePath / PackageStoragePath (for DB fallback and error hints)
$userSettingsPath = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA "DeployPortal\usersettings.json" } else { Join-Path $env:USERPROFILE ".local\DeployPortal\usersettings.json" }
if (Test-Path -LiteralPath $userSettingsPath -PathType Leaf) {
    $fileSettings = Get-Content -LiteralPath $userSettingsPath -Raw | ConvertFrom-Json
    if ($fromPortal) { $userSettings.DatabasePath = $fileSettings.DatabasePath; $userSettings.PackageStoragePath = $fileSettings.PackageStoragePath }
    else { $userSettings = $fileSettings }
}
if (-not $userSettings) { $userSettings = @{ PackageStoragePath = ""; DatabasePath = "" } }

# Database path: from settings only if file exists, else try paths relative to script (no dependency on current directory)
$scriptParent = (Split-Path -Parent $PSScriptRoot)
$tryDirs = @(
    (Join-Path $PSScriptRoot "..\publish\deploy-portal.db"),
    (Join-Path $PSScriptRoot "..\deploy-portal.db"),
    (Join-Path $scriptParent "deploy-portal.db")
)
$DatabasePath = $null
if ($userSettings.DatabasePath -and (Test-Path -LiteralPath $userSettings.DatabasePath -PathType Leaf)) {
    $DatabasePath = $userSettings.DatabasePath
}
if (-not $DatabasePath) {
    foreach ($p in $tryDirs) {
        $abs = $null
        try { $abs = (Resolve-Path -LiteralPath $p -ErrorAction Stop).Path } catch {}
        if ($abs -and (Test-Path -LiteralPath $abs -PathType Leaf)) {
            $DatabasePath = $abs
            break
        }
    }
}

# ---------- Resolve package from DB if needed ----------
if (-not $PackagePath -and $DatabasePath -and (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
    $sqlite3 = $null
    foreach ($candidate in @("sqlite3", "C:\Program Files\Git\usr\bin\sqlite3.exe")) {
        if ($candidate -eq "sqlite3") {
            $e = Get-Command sqlite3 -ErrorAction SilentlyContinue
            if ($e) { $sqlite3 = $e.Source; break }
        } elseif (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $sqlite3 = $candidate
            break
        }
    }
    if ($sqlite3) {
        $query = if ($PackageName) {
            $safe = $PackageName.Replace("'", "''")
            "SELECT Name, StoredFilePath FROM Packages WHERE Name = '$safe' ORDER BY UploadedAt DESC LIMIT 1"
        } else {
            "SELECT Name, StoredFilePath FROM Packages ORDER BY UploadedAt DESC LIMIT 1"
        }
        $result = & $sqlite3 -separator "|" $DatabasePath $query 2>$null
        if ($result) {
            $parts = $result.Trim().Split("|", 2)
            if (-not $PackageName) { $PackageName = $parts[0] }
            $PackagePath = $parts[1]
        }
    }
}

if (-not $PackagePath -or -not (Test-Path -LiteralPath $PackagePath)) {
    Write-Host "Package path not found or not set." -ForegroundColor Red
    if (-not $DatabasePath) {
        Write-Host "  Database not found (tried: $($tryDirs -join ', '))."
    } elseif (-not (Get-Command sqlite3 -ErrorAction SilentlyContinue) -and -not (Test-Path "C:\Program Files\Git\usr\bin\sqlite3.exe" -ErrorAction SilentlyContinue)) {
        Write-Host "  sqlite3 not found (install Git for Windows for sqlite3, or pass -PackagePath)."
    }
    Write-Host "  Database: $DatabasePath"
    $storagePath = $userSettings.PackageStoragePath
    if ($storagePath -and (Test-Path -LiteralPath $storagePath -PathType Container)) {
        Write-Host "  Your PackageStoragePath: $storagePath (use -PackagePath with a .zip from there)."
    }
    Write-Host ""
    Write-Host "Example (replace with your .zip path and run from repo root):" -ForegroundColor Yellow
    Write-Host "  .\scripts\upload-universal-package-from-portal.ps1 -PackagePath 'C:\Packages\your-package.zip' -PackageName 'MyDeployablePackage_1.0.0.0' -Version '1.0.1770920439'"
    exit 1
}

# Defaults as in Deploy dialog screenshot (PPackages, MyDeployablePackage_1.0.0.0, 1.0.1770920439)
if (-not $PackageName) {
    $PackageName = "MyDeployablePackage_1.0.0.0"
}
if (-not $Version) {
    $Version = "1.0.1770920439"
}

$orgUrl = "https://dev.azure.com/$Organization"

# ---------- Resolve az (same as DeployPortal) ----------
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

# ---------- Extract .zip to temp if needed ----------
$pathToPublish = $PackagePath
$tempDir = $null
if ($PackagePath -match '\.zip$') {
    $tempDir = Join-Path $env:TEMP "DeployPortal_Upack_$(New-Guid | ForEach-Object { $_.Guid.Replace('-','') })"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Write-Host "Extracting zip to $tempDir ..."
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $tempDir -Force
    $pathToPublish = $tempDir
}

if ($FeedOverride) { $Feed = $FeedOverride }
# Universal package name must be lowercase (Azure Artifacts requirement)
$PackageNameForAz = $PackageName.ToLowerInvariant()
try {
    $argList = @(
        "artifacts", "universal", "publish",
        "--organization", $orgUrl,
        "--project",      $Project,
        "--scope",        "project",
        "--feed",         $Feed,
        "--name",         $PackageNameForAz,
        "--version",      $Version,
        "--path",         $pathToPublish
    )

    $cmdLine = "`"$azPath`" " + ($argList | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join " "
    Write-Host "Parameters from portal:" -ForegroundColor Cyan
    Write-Host "  Org:     $Organization"
    Write-Host "  Project: $Project"
    Write-Host "  Feed:    $Feed"
    Write-Host "  Name:    $PackageNameForAz (Universal requires lowercase)"
    Write-Host "  Version: $Version"
    Write-Host "  Path:    $pathToPublish"
    Write-Host ""
    Write-Host "Full command (copy to CMD if needed):" -ForegroundColor Cyan
    Write-Host $cmdLine
    Write-Host ""

    if ($NoRun) {
        Write-Host "Skipping run (-NoRun). Remove -NoRun to execute."
        exit 0
    }

    $prevPat = $env:AZURE_DEVOPS_EXT_PAT
    if ($Pat) {
        $env:AZURE_DEVOPS_EXT_PAT = $Pat.Trim()
        Write-Host "Using PAT for az (same as in Deploy Portal)." -ForegroundColor Green
    } else {
        Write-Host "No -Pat passed: az will use its own login (run 'az login' if needed)." -ForegroundColor Yellow
    }
    Write-Host "Running..." -ForegroundColor Yellow
    try {
        & $azPath @argList
    } finally {
        if ($null -ne $prevPat) { $env:AZURE_DEVOPS_EXT_PAT = $prevPat } else { Remove-Item -LiteralPath env:AZURE_DEVOPS_EXT_PAT -ErrorAction SilentlyContinue }
    }
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Host "Exit code: $exitCode" -ForegroundColor Red
        exit $exitCode
    }
    Write-Host "Upload done." -ForegroundColor Green

    # Create release if DefinitionId and ArtifactAlias are set
    if ($DefinitionId -gt 0 -and $ArtifactAlias -and $Pat) {
        $base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$($Pat.Trim())"))
        $headers = @{
            Authorization = "Basic $base64Auth"
            "Content-Type" = "application/json"
        }
        $encOrg = [Uri]::EscapeDataString($Organization)
        $encProj = [Uri]::EscapeDataString($Project)
        $description = "DeployPortal script: $PackageNameForAz v$Version"
        $artifactVersions = @{ $ArtifactAlias.Trim() = $Version }

        # Get release definition to find all artifacts that require version at creation
        $defUrl = "https://vsrm.dev.azure.com/$encOrg/$encProj/_apis/release/definitions/$DefinitionId?api-version=7.1"
        try {
            $def = Invoke-RestMethod -Uri $defUrl -Method Get -Headers $headers -TimeoutSec 15
        } catch {
            Write-Host "Release creation failed (get definition): $($_.Exception.Message)" -ForegroundColor Red
            if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message -ForegroundColor Red }
            exit 1
        }

        $selectDuringCreationTypeId = "selectDuringReleaseCreationType"
        $artifactsList = @($def.artifacts)
        foreach ($art in $artifactsList) {
            $alias = $art.alias
            if (-not $alias) { continue }
            if ($artifactVersions.ContainsKey($alias)) { continue }
            $ref = $art.definitionReference
            $versionTypeId = $ref.defaultVersionType.id
            $versionTypeName = $ref.defaultVersionType.name
            $needsVersionAtCreation = ($versionTypeId -eq $selectDuringCreationTypeId) -or ($versionTypeName -match "release creation|specify at")
            if (-not $needsVersionAtCreation) { continue }

            $ver = $AdditionalArtifactVersions[$alias]
            if ($ver) {
                $artifactVersions[$alias] = $ver
                continue
            }
            if ($art.type -eq "Build") {
                $buildDefId = $ref.definition.id
                if ($buildDefId) {
                    $buildsUrl = "https://dev.azure.com/$encOrg/$encProj/_apis/build/builds?definitions=$buildDefId&`$top=1&resultFilter=succeeded&api-version=7.1"
                    try {
                        $builds = Invoke-RestMethod -Uri $buildsUrl -Method Get -Headers $headers -TimeoutSec 10
                        if ($builds.value -and $builds.value.Count -gt 0) {
                            $artifactVersions[$alias] = [string]$builds.value[0].id
                            Write-Host "Using latest build $($builds.value[0].id) for artifact '$alias'." -ForegroundColor Gray
                        }
                    } catch { }
                }
            }
            if (-not $artifactVersions.ContainsKey($alias)) {
                Write-Host "Release creation failed: artifact '$alias' has 'Specify at release creation' but no version provided. Use -AdditionalArtifactVersions @{ '$alias' = '<version-or-build-id>' }" -ForegroundColor Red
                exit 1
            }
        }

        $artifactsJson = ($artifactVersions.GetEnumerator() | ForEach-Object {
            $a = $_.Key -replace '\\','\\\\' -replace '"','\"'
            $v = $_.Value -replace '\\','\\\\' -replace '"','\"'
            "{`"alias`":`"$a`",`"instanceReference`":{`"id`":`"$v`",`"name`":`"$v`"}}"
        }) -join ","
        $descEsc = $description -replace '\\','\\\\' -replace '"','\"'
        $bodyJson = "{`"definitionId`":$DefinitionId,`"description`":`"$descEsc`",`"isDraft`":false,`"artifacts`":[$artifactsJson]}"
        $releaseUrl = "https://vsrm.dev.azure.com/$encOrg/$encProj/_apis/release/releases?api-version=7.1"
        try {
            $releaseResp = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $bodyJson -TimeoutSec 30
            $releaseId = $releaseResp.id
            $webUrl = "https://dev.azure.com/$Organization/$Project/_release?releaseId=$releaseId"
            Write-Host "Release created: $webUrl" -ForegroundColor Green
        } catch {
            Write-Host "Release creation failed: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message -ForegroundColor Red }
            exit 1
        }
    } elseif ($DefinitionId -gt 0 -or $ArtifactAlias) {
        Write-Host "To start a release pass -DefinitionId, -ArtifactAlias and -Pat." -ForegroundColor Yellow
    } else {
        Write-Host ""
        Write-Host "Release pipeline was not started. To create a release after upload, run with:" -ForegroundColor Cyan
        Write-Host "  -DefinitionId <release-definition-id> -ArtifactAlias <alias> -Pat <token>" -ForegroundColor Cyan
        Write-Host "See documents/Release-Pipeline-Universal-Package.md for how to get DefinitionId and alias." -ForegroundColor Gray
    }
}
finally {
    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
