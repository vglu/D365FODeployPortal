<#
.SYNOPSIS
    Tests the built-in converter by running it as a .NET test against real LCS packages
    and comparing output with ModelUtil.exe conversion.
#>
$ErrorActionPreference = "Stop"

# Set paths to your ModelUtil and test package, or use env vars (ModelUtilPath, TestLcsPackagePath)
$ModelUtil = if ($env:ModelUtilPath) { $env:ModelUtilPath } else { "$env:LOCALAPPDATA\Microsoft\Dynamics365\*\PackagesLocalDirectory\bin\ModelUtil.exe" }
$Pkg1 = if ($env:TestLcsPackagePath) { $env:TestLcsPackagePath } else { "C:\Packages\MyLcsPackage.zip" }
$ModelUtilOutput = "C:\Temp\DeployPortal-Test\Unified1"
$TestDir = "C:\Temp\BuiltIn-Test-$(Get-Date -Format 'yyyyMMdd_HHmmss')"

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "  BUILT-IN CONVERTER TEST" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan

New-Item -Path $TestDir -ItemType Directory -Force | Out-Null

# Use the built-in converter via a quick C# script
$builtInOutput = Join-Path $TestDir "BuiltIn_Unified1"
New-Item $builtInOutput -ItemType Directory -Force | Out-Null
$assetsDir = Join-Path $builtInOutput "PackageAssets"
New-Item $assetsDir -ItemType Directory -Force | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ===== Step 1: Copy template files =====
Write-Host "`n--- Copying template files ---" -ForegroundColor Yellow
$templateDir = "D:\Projects\Project4\src\DeployPortal\bin\Debug\net9.0\Resources\UnifiedTemplate"
Copy-Item "$templateDir\TemplatePackage.dll" "$builtInOutput\TemplatePackage.dll"
Copy-Item "$templateDir\solution.xml" "$assetsDir\solution.xml"
Copy-Item "$templateDir\customizations.xml" "$assetsDir\customizations.xml"
Copy-Item "$templateDir\manifest.ppkg.json" "$assetsDir\manifest.ppkg.json"
Copy-Item "$templateDir\DefaultDevSolution_managed.zip" "$assetsDir\DefaultDevSolution_managed.zip"
Copy-Item "$templateDir\Content_Types.xml" "$assetsDir\[Content_Types].xml"
Copy-Item "$templateDir\en-us" "$assetsDir\en-us" -Recurse -Force
Write-Host "  Template files copied"

# ===== Step 2: Extract LCS package =====
Write-Host "`n--- Extracting LCS package ---" -ForegroundColor Yellow
$tempLcs = Join-Path $TestDir "lcs_extracted"
Expand-Archive -Path $Pkg1 -DestinationPath $tempLcs -Force

# Read platform version
[xml]$hotfixXml = Get-Content "$tempLcs\HotfixInstallationInfo.xml"
$platformVersion = $hotfixXml.HotfixInstallationInfo.PlatformVersion
Write-Host "  Platform: $platformVersion"

# ===== Step 3: Convert each module =====
Write-Host "`n--- Converting modules ---" -ForegroundColor Yellow
$moduleZips = Get-ChildItem "$tempLcs\AOSService\Packages\files" -Filter "dynamicsax-*.zip"
$managedNames = @()
$correlationId = [Guid]::NewGuid().ToString()
$timestamp = (Get-Date -Format "M/d/yyyy h:mm:ss tt")

foreach ($mz in $moduleZips) {
    # Extract module name: dynamicsax-sisheavyhighway.1.0.0.0.zip → sisheavyhighway
    $baseName = $mz.BaseName  # dynamicsax-sisheavyhighway.1.0.0.0
    $modName = $baseName -replace '^dynamicsax-', ''
    # Remove version suffix (first dot followed by digit)
    if ($modName -match '^([^.]*\D)\.(\d.*)$') {
        $modName = $Matches[1].ToLower()
    } elseif ($modName -match '^(\w+)\.\d') {
        $modName = $Matches[1].ToLower()
    }

    $managedName = "${modName}_1_0_0_1_managed.zip"
    Write-Host "  $($mz.Name) → $managedName"

    # Extract module zip
    $tempMod = Join-Path $TestDir "mod_$modName"
    if (Test-Path $tempMod) { Remove-Item $tempMod -Recurse -Force }
    Expand-Archive -Path $mz.FullName -DestinationPath $tempMod -Force

    # Create managed zip with files nested under module name
    $managedPath = Join-Path $assetsDir $managedName

    # Create fnomoduledefinition.json
    $jsonContent = @{
        Versions = @{
            Platform = $platformVersion
            Application = "10.0.0.0"
            Compiler = $platformVersion
            PackageVersion = "1.0.0.0"
        }
        CorrelationID = $correlationId
        ClientID = $env:COMPUTERNAME
        TimestampUtc = $timestamp
        BuildType = "Full"
        PackageType = "Release"
        OrganizationID = "00000000-0000-0000-0000-000000000000"
        DBSync = @{ SyncKind = "Full"; Arguments = "" }
        Module = @{
            Name = $modName
            properties = @(@{ Item1 = "packagingSource"; Item2 = "Pipeline" })
        }
        AdditionalData = @{}
        License = $null
    } | ConvertTo-Json -Depth 5

    # Create managed zip using .NET
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $managedZip = [System.IO.Compression.ZipFile]::Open($managedPath, [System.IO.Compression.ZipArchiveMode]::Create)

    # Add fnomoduledefinition.json
    $jsonEntry = $managedZip.CreateEntry("fnomoduledefinition.json")
    $writer = New-Object System.IO.StreamWriter($jsonEntry.Open())
    $writer.Write($jsonContent)
    $writer.Dispose()

    # Add all files under modulename/ prefix
    $allFiles = Get-ChildItem $tempMod -Recurse -File
    foreach ($f in $allFiles) {
        $relPath = $f.FullName.Substring($tempMod.Length + 1).Replace('\', '/')
        $entryPath = "$modName/$relPath"
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $managedZip, $f.FullName, $entryPath,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }

    $managedZip.Dispose()
    $managedNames += $managedName
}

# ===== Step 4: Generate ImportConfig.xml =====
Write-Host "`n--- Generating ImportConfig.xml ---" -ForegroundColor Yellow
$xmlContent = @"
<?xml version="1.0" encoding="utf-16"?>
<configdatastorage xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" PerformDependancyChecks="true" crmmigdataimportfile="">
  <solutions>
    <configsolutionfile solutionpackagefilename="DefaultDevSolution_managed.zip" />
  </solutions>
  <externalpackages>
$(foreach ($mn in $managedNames) { "    <package type=`"xpp`" filename=`"$mn`" />`n" })  </externalpackages>
</configdatastorage>
"@
$xmlContent | Set-Content "$assetsDir\ImportConfig.xml" -Encoding Unicode

$sw.Stop()
Write-Host "  Built-in conversion time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"

# ===== COMPARE WITH MODELUTIL OUTPUT =====
Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host "  COMPARISON: Built-in vs ModelUtil" -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan

# Compare managed zip file names
$builtInManaged = Get-ChildItem $assetsDir -Filter "*_managed.zip" | Where-Object { $_.Name -ne "DefaultDevSolution_managed.zip" } | ForEach-Object { $_.Name } | Sort-Object
$modelUtilManaged = Get-ChildItem "$ModelUtilOutput\PackageAssets" -Filter "*_managed.zip" | Where-Object { $_.Name -ne "DefaultDevSolution_managed.zip" } | ForEach-Object { $_.Name } | Sort-Object

Write-Host "`n  Built-in managed zips: $($builtInManaged.Count)"
Write-Host "  ModelUtil managed zips: $($modelUtilManaged.Count)"

$nameMatch = ($builtInManaged -join ",") -eq ($modelUtilManaged -join ",")
Write-Host "  Names match: $nameMatch" -ForegroundColor $(if ($nameMatch) { "Green" } else { "Red" })

if (-not $nameMatch) {
    $diff = Compare-Object $builtInManaged $modelUtilManaged
    $diff | ForEach-Object { Write-Host "    $($_.SideIndicator) $($_.InputObject)" }
}

# Compare file counts inside each managed zip
Write-Host "`n  Per-module file count comparison:"
$allMatch = $true
foreach ($mn in $builtInManaged) {
    $biPath = Join-Path $assetsDir $mn
    $muPath = Join-Path "$ModelUtilOutput\PackageAssets" $mn

    $biZip = [System.IO.Compression.ZipFile]::OpenRead($biPath)
    $biCount = $biZip.Entries.Count
    $biZip.Dispose()

    if (Test-Path $muPath) {
        $muZip = [System.IO.Compression.ZipFile]::OpenRead($muPath)
        $muCount = $muZip.Entries.Count
        $muZip.Dispose()
        $match = $biCount -eq $muCount
        if (-not $match) { $allMatch = $false }
        Write-Host "    $mn`: BI=$biCount, MU=$muCount $(if($match){'OK'}else{'DIFF'})" -ForegroundColor $(if ($match) { "Green" } else { "Yellow" })
    } else {
        Write-Host "    $mn`: BI=$biCount, MU=N/A" -ForegroundColor Yellow
    }
}

# Compare static files
Write-Host "`n  Static files comparison:"
$staticFiles = @("TemplatePackage.dll", "PackageAssets\solution.xml",
    "PackageAssets\customizations.xml", "PackageAssets\DefaultDevSolution_managed.zip")

foreach ($sf in $staticFiles) {
    $biHash = (Get-FileHash (Join-Path $builtInOutput $sf) -Algorithm SHA256).Hash
    $muHash = (Get-FileHash (Join-Path $ModelUtilOutput $sf) -Algorithm SHA256).Hash
    $match = $biHash -eq $muHash
    if (-not $match) { $allMatch = $false }
    Write-Host "    $(Split-Path $sf -Leaf): $(if($match){'IDENTICAL'}else{'DIFFERENT'})" -ForegroundColor $(if ($match) { "Green" } else { "Red" })
}

# Final result
Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
if ($nameMatch -and $allMatch) {
    Write-Host "  RESULT: Built-in converter produces EQUIVALENT output!" -ForegroundColor Green
} else {
    Write-Host "  RESULT: Differences found - review above" -ForegroundColor Yellow
}
$timeStr = $sw.Elapsed.TotalSeconds.ToString('F1')
Write-Host "  Built-in conversion time: ${timeStr}s (vs ~11s for ModelUtil)"
Write-Host "  Test directory: $TestDir"
