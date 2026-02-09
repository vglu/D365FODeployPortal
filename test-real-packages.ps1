<#
.SYNOPSIS
    Full integration test with real LCS packages:
    1. Convert LCS1 → Unified1
    2. Convert LCS2 → Unified2
    3. Merge LCS1 + LCS2 → MergedLCS → Convert → UnifiedFromMergedLCS
    4. Merge Unified1 + Unified2 → MergedUnified
    5. Compare: UnifiedFromMergedLCS vs MergedUnified
#>

$ErrorActionPreference = "Stop"
$ModelUtil = "C:\Users\vetal\AppData\Local\Microsoft\Dynamics365\10.0.2428.63\PackagesLocalDirectory\bin\ModelUtil.exe"
$Pkg1 = "D:\DeployPortal\Packages\20260208_232056_3b16a76e12ce417988a3b03b1298427b_PCM-DeployableRuntime_2026.1.8.3.zip"
$Pkg2 = "D:\DeployPortal\Packages\20260208_232105_4f04faa4d8384c3cbd3d4cb6364cb0df_ALOPS-DeployableRuntime_2026.1.8.3.zip"
$TestDir = "D:\Temp\DeployPortal-Test-$(Get-Date -Format 'yyyyMMdd_HHmmss')"

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "  FULL INTEGRATION TEST WITH REAL PACKAGES" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "ModelUtil: $ModelUtil"
Write-Host "Package 1: $Pkg1"
Write-Host "Package 2: $Pkg2"
Write-Host "Test Dir:  $TestDir"
Write-Host ""

# Verify prerequisites
if (-not (Test-Path $ModelUtil)) { throw "ModelUtil.exe not found at $ModelUtil" }
if (-not (Test-Path $Pkg1)) { throw "Package 1 not found" }
if (-not (Test-Path $Pkg2)) { throw "Package 2 not found" }

New-Item -Path $TestDir -ItemType Directory -Force | Out-Null

$results = @{}
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# =====================================================
# STEP 1: Convert LCS1 → Unified1
# =====================================================
Write-Host "`n--- STEP 1: Convert LCS1 → Unified1 ---" -ForegroundColor Yellow
$uni1Dir = Join-Path $TestDir "Unified1"
New-Item -Path $uni1Dir -ItemType Directory -Force | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $ModelUtil -convertToUnifiedPackage -file="$Pkg1" -outputpath="$uni1Dir" 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
$sw.Stop()

$uni1DllExists = Test-Path (Join-Path $uni1Dir "TemplatePackage.dll")
$uni1Files = (Get-ChildItem $uni1Dir -Recurse -File).Count
Write-Host "  TemplatePackage.dll exists: $uni1DllExists" -ForegroundColor $(if ($uni1DllExists) { "Green" } else { "Red" })
Write-Host "  Total files: $uni1Files | Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
$results["Step1_Convert_LCS1"] = @{ Success = $uni1DllExists; Files = $uni1Files; Time = $sw.Elapsed.TotalSeconds }

# =====================================================
# STEP 2: Convert LCS2 → Unified2
# =====================================================
Write-Host "`n--- STEP 2: Convert LCS2 → Unified2 ---" -ForegroundColor Yellow
$uni2Dir = Join-Path $TestDir "Unified2"
New-Item -Path $uni2Dir -ItemType Directory -Force | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $ModelUtil -convertToUnifiedPackage -file="$Pkg2" -outputpath="$uni2Dir" 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
$sw.Stop()

$uni2DllExists = Test-Path (Join-Path $uni2Dir "TemplatePackage.dll")
$uni2Files = (Get-ChildItem $uni2Dir -Recurse -File).Count
Write-Host "  TemplatePackage.dll exists: $uni2DllExists" -ForegroundColor $(if ($uni2DllExists) { "Green" } else { "Red" })
Write-Host "  Total files: $uni2Files | Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
$results["Step2_Convert_LCS2"] = @{ Success = $uni2DllExists; Files = $uni2Files; Time = $sw.Elapsed.TotalSeconds }

# =====================================================
# STEP 3: Merge LCS1 + LCS2 → MergedLCS
# =====================================================
Write-Host "`n--- STEP 3: Merge LCS1 + LCS2 → MergedLCS ---" -ForegroundColor Yellow
$mergedLcsDir = Join-Path $TestDir "MergedLCS"
$tempMergeDir = Join-Path $TestDir "TempMerge"

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Extract first package
Write-Host "  Extracting LCS1 as base..."
Expand-Archive -Path $Pkg1 -DestinationPath $mergedLcsDir -Force

# Extract second package
Write-Host "  Extracting LCS2..."
Expand-Archive -Path $Pkg2 -DestinationPath $tempMergeDir -Force

# Copy AOSService content from pkg2 into merged
Write-Host "  Copying AOSService content..."
$sourceAOS = Join-Path $tempMergeDir "AOSService"
$targetAOS = Join-Path $mergedLcsDir "AOSService"
if (Test-Path $sourceAOS) {
    Copy-Item -Path "$sourceAOS\*" -Destination $targetAOS -Recurse -Force
}

# Merge HotfixInstallationInfo.xml
Write-Host "  Merging HotfixInstallationInfo.xml..."
$xml1Path = Join-Path $mergedLcsDir "HotfixInstallationInfo.xml"
$xml2Path = Join-Path $tempMergeDir "HotfixInstallationInfo.xml"

if ((Test-Path $xml1Path) -and (Test-Path $xml2Path)) {
    [xml]$doc1 = Get-Content $xml1Path
    [xml]$doc2 = Get-Content $xml2Path

    # Merge MetadataModuleList
    $moduleList1 = $doc1.HotfixInstallationInfo.MetadataModuleList
    $moduleList2 = $doc2.HotfixInstallationInfo.MetadataModuleList
    foreach ($mod in $moduleList2.string) {
        $newNode = $doc1.CreateElement("string")
        $newNode.InnerText = $mod
        $moduleList1.AppendChild($newNode) | Out-Null
    }

    # Merge AllComponentList
    $compList1 = $doc1.HotfixInstallationInfo.AllComponentList
    $compList2 = $doc2.HotfixInstallationInfo.AllComponentList
    foreach ($comp in $compList2.ArrayOfString) {
        $imported = $doc1.ImportNode($comp, $true)
        $compList1.AppendChild($imported) | Out-Null
    }

    $doc1.Save($xml1Path)
}

$sw.Stop()

# Verify merge
$mergedModules = ([xml](Get-Content $xml1Path)).HotfixInstallationInfo.MetadataModuleList.string
$mergedNupkgs = (Get-ChildItem (Join-Path $mergedLcsDir "AOSService\Packages") -Filter "*.nupkg").Count
Write-Host "  Modules in merged XML: $($mergedModules.Count)" -ForegroundColor Green
Write-Host "  Nupkg files: $mergedNupkgs"
Write-Host "  Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
$results["Step3_Merge_LCS"] = @{ Success = $mergedModules.Count -eq 15; ModuleCount = $mergedModules.Count; Time = $sw.Elapsed.TotalSeconds }

# Create merged ZIP
$mergedLcsZip = Join-Path $TestDir "MergedLCS.zip"
Write-Host "  Creating merged ZIP..."
Compress-Archive -Path "$mergedLcsDir\*" -DestinationPath $mergedLcsZip -Force

# Cleanup temp
Remove-Item $tempMergeDir -Recurse -Force -ErrorAction SilentlyContinue

# =====================================================
# STEP 4: Convert MergedLCS → UnifiedFromMergedLCS
# =====================================================
Write-Host "`n--- STEP 4: Convert MergedLCS → UnifiedFromMergedLCS ---" -ForegroundColor Yellow
$uniFromMergedDir = Join-Path $TestDir "UnifiedFromMergedLCS"
New-Item -Path $uniFromMergedDir -ItemType Directory -Force | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $ModelUtil -convertToUnifiedPackage -file="$mergedLcsZip" -outputpath="$uniFromMergedDir" 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
$sw.Stop()

$uniMergedDllExists = Test-Path (Join-Path $uniFromMergedDir "TemplatePackage.dll")
$uniMergedFiles = (Get-ChildItem $uniFromMergedDir -Recurse -File).Count
Write-Host "  TemplatePackage.dll exists: $uniMergedDllExists" -ForegroundColor $(if ($uniMergedDllExists) { "Green" } else { "Red" })
Write-Host "  Total files: $uniMergedFiles | Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
$results["Step4_Convert_MergedLCS"] = @{ Success = $uniMergedDllExists; Files = $uniMergedFiles; Time = $sw.Elapsed.TotalSeconds }

# =====================================================
# STEP 5: Merge Unified1 + Unified2 → MergedUnified
# =====================================================
Write-Host "`n--- STEP 5: Merge Unified1 + Unified2 → MergedUnified ---" -ForegroundColor Yellow
$mergedUniDir = Join-Path $TestDir "MergedUnified"

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Copy Unified1 as base
Write-Host "  Copying Unified1 as base..."
Copy-Item -Path $uni1Dir -Destination $mergedUniDir -Recurse -Force

# Copy Unified2 on top (later overrides)
Write-Host "  Copying Unified2 on top..."
$uni2Items = Get-ChildItem $uni2Dir -Recurse -File
foreach ($file in $uni2Items) {
    $relativePath = $file.FullName.Substring($uni2Dir.Length + 1)
    $destPath = Join-Path $mergedUniDir $relativePath
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
    Copy-Item -Path $file.FullName -Destination $destPath -Force
}

# Update ImportConfig.xml files
Write-Host "  Updating ImportConfig.xml files..."
$xmlFiles = Get-ChildItem $mergedUniDir -Filter "ImportConfig.xml" -Recurse
$totalAdded = 0
foreach ($xmlFile in $xmlFiles) {
    $folder = $xmlFile.DirectoryName
    [xml]$doc = Get-Content $xmlFile.FullName

    # Find externalpackages node
    $ep = $doc.SelectSingleNode("//externalpackages")
    if ($null -eq $ep) {
        $configStorage = $doc.SelectSingleNode("//configdatastorage")
        if ($null -eq $configStorage) { $configStorage = $doc.DocumentElement }
        $ep = $doc.CreateElement("externalpackages")
        $configStorage.AppendChild($ep) | Out-Null
    }

    # Get existing filenames
    $existing = @()
    foreach ($pkg in $ep.SelectNodes("package")) {
        $existing += $pkg.GetAttribute("filename")
    }

    # Find managed zips
    $managedZips = Get-ChildItem $folder -Filter "*_managed.zip" -ErrorAction SilentlyContinue
    foreach ($mz in $managedZips) {
        if ($mz.Name -notin $existing) {
            $newNode = $doc.CreateElement("package")
            $newNode.SetAttribute("type", "xpp")
            $newNode.SetAttribute("filename", $mz.Name)
            $ep.AppendChild($newNode) | Out-Null
            $totalAdded++
            Write-Host "    Added: $($mz.Name) to $($xmlFile.Name) in $(Split-Path $folder -Leaf)" -ForegroundColor DarkGreen
        }
    }

    $doc.Save($xmlFile.FullName)
}
$sw.Stop()

$mergedUniFiles = (Get-ChildItem $mergedUniDir -Recurse -File).Count
Write-Host "  ImportConfig entries added: $totalAdded"
Write-Host "  Total files: $mergedUniFiles | Time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
$results["Step5_Merge_Unified"] = @{ Success = $true; Files = $mergedUniFiles; EntriesAdded = $totalAdded; Time = $sw.Elapsed.TotalSeconds }

# =====================================================
# STEP 6: COMPARE — UnifiedFromMergedLCS vs MergedUnified
# =====================================================
Write-Host "`n--- STEP 6: COMPARE RESULTS ---" -ForegroundColor Yellow

# Get managed zips from both
$pathA = $uniFromMergedDir  # LCS merged then converted
$pathB = $mergedUniDir       # Converted separately then UDE-merged

$managedA = Get-ChildItem $pathA -Filter "*_managed.zip" -Recurse | ForEach-Object { $_.Name } | Sort-Object
$managedB = Get-ChildItem $pathB -Filter "*_managed.zip" -Recurse | ForEach-Object { $_.Name } | Sort-Object

Write-Host "`n  PATH A (Merge LCS → Convert): $($managedA.Count) managed packages"
$managedA | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

Write-Host "`n  PATH B (Convert → Merge UDE): $($managedB.Count) managed packages"
$managedB | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

# Compare
$inAnotB = $managedA | Where-Object { $_ -notin $managedB }
$inBnotA = $managedB | Where-Object { $_ -notin $managedA }

if ($inAnotB.Count -eq 0 -and $inBnotA.Count -eq 0 -and $managedA.Count -eq $managedB.Count) {
    Write-Host "`n  RESULT: IDENTICAL managed packages!" -ForegroundColor Green
    $results["Step6_Compare"] = @{ Success = $true; ManagedCount = $managedA.Count }
} else {
    Write-Host "`n  RESULT: DIFFERENCES FOUND!" -ForegroundColor Red
    if ($inAnotB.Count -gt 0) { Write-Host "  In A but not B:" -ForegroundColor Red; $inAnotB | ForEach-Object { Write-Host "    $_" } }
    if ($inBnotA.Count -gt 0) { Write-Host "  In B but not A:" -ForegroundColor Red; $inBnotA | ForEach-Object { Write-Host "    $_" } }
    $results["Step6_Compare"] = @{ Success = $false; InAnotB = $inAnotB.Count; InBnotA = $inBnotA.Count }
}

# Also compare ImportConfig.xml content
Write-Host "`n  ImportConfig.xml comparison:"
$xmlA = Get-ChildItem $pathA -Filter "ImportConfig.xml" -Recurse
$xmlB = Get-ChildItem $pathB -Filter "ImportConfig.xml" -Recurse

foreach ($xa in $xmlA) {
    [xml]$docA = Get-Content $xa.FullName
    $pkgsA = $docA.SelectNodes("//externalpackages/package") | ForEach-Object { $_.GetAttribute("filename") } | Sort-Object
    Write-Host "    Path A ($($xa.DirectoryName | Split-Path -Leaf)): $($pkgsA.Count) entries -> $($pkgsA -join ', ')" -ForegroundColor DarkGray
}

foreach ($xb in $xmlB) {
    [xml]$docB = Get-Content $xb.FullName
    $pkgsB = $docB.SelectNodes("//externalpackages/package") | ForEach-Object { $_.GetAttribute("filename") } | Sort-Object
    Write-Host "    Path B ($($xb.DirectoryName | Split-Path -Leaf)): $($pkgsB.Count) entries -> $($pkgsB -join ', ')" -ForegroundColor DarkGray
}

# =====================================================
# SUMMARY
# =====================================================
$stopwatch.Stop()
Write-Host "`n" + ("=" * 70) -ForegroundColor Cyan
Write-Host "  TEST RESULTS SUMMARY" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan

Write-Host "`n  Step 1 - Convert LCS1:       $(if ($results['Step1_Convert_LCS1'].Success) { 'PASS' } else { 'FAIL' }) ($($results['Step1_Convert_LCS1'].Files) files, $($results['Step1_Convert_LCS1'].Time.ToString('F1'))s)" -ForegroundColor $(if ($results['Step1_Convert_LCS1'].Success) { 'Green' } else { 'Red' })
Write-Host "  Step 2 - Convert LCS2:       $(if ($results['Step2_Convert_LCS2'].Success) { 'PASS' } else { 'FAIL' }) ($($results['Step2_Convert_LCS2'].Files) files, $($results['Step2_Convert_LCS2'].Time.ToString('F1'))s)" -ForegroundColor $(if ($results['Step2_Convert_LCS2'].Success) { 'Green' } else { 'Red' })
Write-Host "  Step 3 - Merge LCS:          $(if ($results['Step3_Merge_LCS'].Success) { 'PASS' } else { 'FAIL' }) ($($results['Step3_Merge_LCS'].ModuleCount) modules)" -ForegroundColor $(if ($results['Step3_Merge_LCS'].Success) { 'Green' } else { 'Red' })
Write-Host "  Step 4 - Convert Merged LCS: $(if ($results['Step4_Convert_MergedLCS'].Success) { 'PASS' } else { 'FAIL' }) ($($results['Step4_Convert_MergedLCS'].Files) files, $($results['Step4_Convert_MergedLCS'].Time.ToString('F1'))s)" -ForegroundColor $(if ($results['Step4_Convert_MergedLCS'].Success) { 'Green' } else { 'Red' })
Write-Host "  Step 5 - Merge Unified:      $(if ($results['Step5_Merge_Unified'].Success) { 'PASS' } else { 'FAIL' }) ($($results['Step5_Merge_Unified'].Files) files, +$($results['Step5_Merge_Unified'].EntriesAdded) XML entries)" -ForegroundColor $(if ($results['Step5_Merge_Unified'].Success) { 'Green' } else { 'Red' })
Write-Host "  Step 6 - Compare:            $(if ($results['Step6_Compare'].Success) { 'PASS' } else { 'FAIL' })" -ForegroundColor $(if ($results['Step6_Compare'].Success) { 'Green' } else { 'Red' })

Write-Host "`n  Total time: $($stopwatch.Elapsed.TotalSeconds.ToString('F1'))s"
Write-Host "  Test directory: $TestDir"

$allPassed = $results.Values | ForEach-Object { $_.Success } | Where-Object { $_ -eq $false }
if ($allPassed.Count -eq 0) {
    Write-Host "`n  ALL TESTS PASSED!" -ForegroundColor Green
} else {
    Write-Host "`n  SOME TESTS FAILED!" -ForegroundColor Red
}
