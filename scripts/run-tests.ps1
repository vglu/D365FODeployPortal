<#
.SYNOPSIS
    Runs unit + integration tests (excludes E2E which requires app config).
    Use -Coverage to collect code coverage; report is in TestResults\<guid>\coverage.cobertura.xml
#>
param(
    [switch]$Coverage,
    [switch]$IncludeE2E
)

$ProjectRoot = Split-Path $PSScriptRoot -Parent
$filter = "FullyQualifiedName!~E2ETests"
if (-not $IncludeE2E) {
    $filter += "&FullyQualifiedName!~ConvertRealLcsPackage_FromEnv"
}
$testProj = Join-Path $ProjectRoot "src\DeployPortal.Tests\DeployPortal.Tests.csproj"
$args = @(
    "test", $testProj,
    "--filter", $filter
)
if ($Coverage) {
    $args += @("--collect:`"XPlat Code Coverage`"", "--results-directory", (Join-Path $ProjectRoot "TestResults"))
    $runsettings = Join-Path $ProjectRoot "coverlet.runsettings"
    if (Test-Path $runsettings) {
        $args += @("--settings", $runsettings)
    }
}

Push-Location $ProjectRoot
& dotnet @args
$exitCode = $LASTEXITCODE
Pop-Location
if ($Coverage -and $exitCode -eq 0) {
    $latest = Get-ChildItem (Join-Path $ProjectRoot "TestResults") -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        $cov = Join-Path $latest.FullName "coverage.cobertura.xml"
        if (Test-Path $cov) {
            Write-Host ""
            Write-Host "Coverage report: $cov" -ForegroundColor Cyan
        }
    }
}
exit $exitCode
