<#
.SYNOPSIS
    Runs unit + integration tests (excludes E2E which requires app config).
    Use -Coverage to collect code coverage; report is in TestResults\<guid>\coverage.cobertura.xml
#>
param(
    [switch]$Coverage,
    [switch]$IncludeE2E
)

$filter = "FullyQualifiedName!~E2ETests"
if (-not $IncludeE2E) {
    $filter += "&FullyQualifiedName!~ConvertRealLcsPackage_FromEnv"
}
$args = @(
    "test", "src\DeployPortal.Tests\DeployPortal.Tests.csproj",
    "--filter", $filter
)
if ($Coverage) {
    $args += @("--collect:`"XPlat Code Coverage`"", "--results-directory", "TestResults")
}

& dotnet @args
$exitCode = $LASTEXITCODE
if ($Coverage -and $exitCode -eq 0) {
    $latest = Get-ChildItem TestResults -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        $cov = Join-Path $latest.FullName "coverage.cobertura.xml"
        if (Test-Path $cov) {
            Write-Host ""
            Write-Host "Coverage report: $cov" -ForegroundColor Cyan
        }
    }
}
exit $exitCode
