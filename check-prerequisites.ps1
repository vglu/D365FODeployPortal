<#
.SYNOPSIS
    Pre-flight check: verifies all required components for DeployPortal.
.DESCRIPTION
    Checks for .NET SDK/Runtime, PAC CLI, ModelUtil.exe, disk space, and
    other prerequisites. Offers download links and install commands for
    any missing components.
.EXAMPLE
    .\check-prerequisites.ps1
    .\check-prerequisites.ps1 -AutoInstall
    .\check-prerequisites.ps1 -Mode Production
#>

param(
    [switch]$AutoInstall,
    [ValidateSet("Development", "Production")]
    [string]$Mode = "Development"
)

# ─────────────────────────────────────────────
#  Helpers
# ─────────────────────────────────────────────

$script:TotalChecks = 0
$script:PassedChecks = 0
$script:WarningChecks = 0
$script:FailedChecks = 0

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "  $('-' * $Text.Length)" -ForegroundColor DarkCyan
}

function Write-CheckResult {
    param(
        [string]$Name,
        [string]$Status,   # OK, WARN, FAIL
        [string]$Details,
        [string]$Action = ""
    )
    $script:TotalChecks++

    $icon = switch ($Status) {
        "OK"   { $script:PassedChecks++;  "[OK]"   }
        "WARN" { $script:WarningChecks++; "[!!]"   }
        "FAIL" { $script:FailedChecks++;  "[FAIL]" }
    }
    $color = switch ($Status) {
        "OK"   { "Green"  }
        "WARN" { "Yellow" }
        "FAIL" { "Red"    }
    }

    Write-Host "    $icon " -ForegroundColor $color -NoNewline
    Write-Host "$Name" -ForegroundColor White -NoNewline
    Write-Host " - $Details" -ForegroundColor Gray

    if ($Action) {
        Write-Host "         -> " -ForegroundColor DarkGray -NoNewline
        Write-Host "$Action" -ForegroundColor Yellow
    }
}

function Find-InPath {
    param([string]$Executable)
    $found = Get-Command $Executable -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    return $null
}

# ─────────────────────────────────────────────
#  Banner
# ─────────────────────────────────────────────

Write-Host ""
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "    DeployPortal - Pre-flight Checks" -ForegroundColor White
Write-Host "    Mode: $Mode" -ForegroundColor Gray
Write-Host "  =============================================" -ForegroundColor Cyan

# ─────────────────────────────────────────────
#  1. Operating System
# ─────────────────────────────────────────────

Write-Header "Operating System"

$osInfo = [System.Environment]::OSVersion
if ($osInfo.Platform -eq "Win32NT") {
    $winVer = [System.Environment]::OSVersion.Version
    Write-CheckResult "Windows" "OK" "Windows $($winVer.Major).$($winVer.Minor) (Build $($winVer.Build))"
} else {
    Write-CheckResult "Windows" "WARN" "Non-Windows OS detected ($($osInfo.Platform)). DeployPortal is designed for Windows."
}

# PowerShell version
$psVer = $PSVersionTable.PSVersion
if ($psVer.Major -ge 5) {
    Write-CheckResult "PowerShell" "OK" "v$psVer"
} else {
    Write-CheckResult "PowerShell" "WARN" "v$psVer (5.1+ recommended)" `
        "Install PowerShell 7: https://aka.ms/install-powershell"
}

# ─────────────────────────────────────────────
#  2. .NET SDK / Runtime
# ─────────────────────────────────────────────

Write-Header ".NET Platform"

$dotnetPath = Find-InPath "dotnet"
if ($dotnetPath) {
    # Get SDK version
    $sdkVersion = $null
    try { $sdkVersion = & dotnet --version 2>$null } catch {}

    if ($sdkVersion) {
        $majorVer = [int]($sdkVersion.Split('.')[0])
        if ($majorVer -ge 9) {
            Write-CheckResult ".NET SDK" "OK" "v$sdkVersion"
        } elseif ($majorVer -ge 8) {
            Write-CheckResult ".NET SDK" "WARN" "v$sdkVersion (v9.0+ required for building)" `
                "Download: https://dotnet.microsoft.com/download/dotnet/9.0"
        } else {
            Write-CheckResult ".NET SDK" "FAIL" "v$sdkVersion (v9.0+ required)" `
                "Download: https://dotnet.microsoft.com/download/dotnet/9.0"
        }
    }

    # Check installed runtimes
    $runtimes = & dotnet --list-runtimes 2>$null
    $aspNetCore9 = $runtimes | Where-Object { $_ -match "Microsoft\.AspNetCore\.App 9\." }
    if ($aspNetCore9) {
        $rtVersion = ($aspNetCore9 | Select-Object -First 1) -replace '.*?(\d+\.\d+\.\d+).*','$1'
        Write-CheckResult "ASP.NET Core Runtime" "OK" "v$rtVersion"
    } else {
        if ($Mode -eq "Production") {
            Write-CheckResult "ASP.NET Core Runtime" "WARN" "9.x not found (bundled in self-contained publish)"
        } else {
            Write-CheckResult "ASP.NET Core Runtime" "FAIL" "9.x not found" `
                "Install: dotnet workload install aspire  OR  https://dotnet.microsoft.com/download/dotnet/9.0"
        }
    }
} else {
    if ($Mode -eq "Production") {
        Write-CheckResult ".NET SDK" "OK" "Not required (self-contained publish bundles runtime)"
    } else {
        Write-CheckResult ".NET SDK" "FAIL" ".NET SDK not found in PATH" `
            "Download .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"
        if ($AutoInstall) {
            Write-Host ""
            Write-Host "         Attempting auto-install via winget..." -ForegroundColor Yellow
            $wingetPath = Find-InPath "winget"
            if ($wingetPath) {
                & winget install Microsoft.DotNet.SDK.9 --accept-package-agreements --accept-source-agreements
            } else {
                Write-Host "         winget not available. Please install manually." -ForegroundColor Red
            }
        }
    }
}

# ─────────────────────────────────────────────
#  3. PAC CLI (Power Platform CLI)
# ─────────────────────────────────────────────

Write-Header "Power Platform CLI (PAC)"

$pacPath = Find-InPath "pac.cmd"
if (-not $pacPath) { $pacPath = Find-InPath "pac.exe" }
if (-not $pacPath) { $pacPath = Find-InPath "pac" }

if ($pacPath) {
    $pacVersion = $null
    try {
        $pacOutput = & $pacPath --version 2>$null
        if ($pacOutput) { $pacVersion = ($pacOutput | Select-Object -First 1).Trim() }
    } catch {}

    if ($pacVersion) {
        Write-CheckResult "PAC CLI" "OK" "v$pacVersion ($pacPath)"
    } else {
        Write-CheckResult "PAC CLI" "OK" "Found at $pacPath"
    }
} else {
    Write-CheckResult "PAC CLI" "FAIL" "Not found in PATH. Required for deployment." `
        "Install: dotnet tool install --global Microsoft.PowerApps.CLI.Tool"

    if ($AutoInstall) {
        Write-Host ""
        Write-Host "         Attempting auto-install..." -ForegroundColor Yellow
        if ($dotnetPath) {
            & dotnet tool install --global Microsoft.PowerApps.CLI.Tool
            if ($LASTEXITCODE -eq 0) {
                Write-Host "         PAC CLI installed successfully!" -ForegroundColor Green
                Write-Host "         Restart your terminal to use 'pac' command." -ForegroundColor Yellow
            }
        } else {
            Write-Host "         Cannot install: .NET SDK required first." -ForegroundColor Red
        }
    }
}

# ─────────────────────────────────────────────
#  4. ModelUtil.exe (optional)
# ─────────────────────────────────────────────

Write-Header "ModelUtil.exe (Optional - for LCS conversion)"

$modelUtilFound = $false
$modelUtilPath = ""

# Check common locations
$searchPaths = @(
    "$env:LOCALAPPDATA\Microsoft\Dynamics365"
    "$env:ProgramFiles\Microsoft Dynamics 365"
    "C:\AOSService\PackagesLocalDirectory\bin"
    "K:\AOSService\PackagesLocalDirectory\bin"
)

foreach ($searchDir in $searchPaths) {
    if (Test-Path $searchDir) {
        $found = Get-ChildItem -Path $searchDir -Filter "ModelUtil.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $modelUtilPath = $found.FullName
            $modelUtilFound = $true
            break
        }
    }
}

# Also check usersettings.json
$settingsFile = Join-Path $PSScriptRoot "src\DeployPortal\bin\Debug\net9.0\usersettings.json"
if (-not (Test-Path $settingsFile)) {
    $settingsFile = Join-Path $PSScriptRoot "publish\usersettings.json"
}
if (Test-Path $settingsFile) {
    try {
        $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
        if ($settings.ModelUtilPath -and (Test-Path $settings.ModelUtilPath)) {
            $modelUtilPath = $settings.ModelUtilPath
            $modelUtilFound = $true
        }
    } catch {}
}

if ($modelUtilFound) {
    Write-CheckResult "ModelUtil.exe" "OK" "Found: $modelUtilPath"
} else {
    Write-CheckResult "ModelUtil.exe" "OK" "Not found (not required - built-in converter is the default)" `
        "Only needed if you switch to 'ModelUtil' converter engine in Settings"
}

# ─────────────────────────────────────────────
#  5. Disk Space
# ─────────────────────────────────────────────

Write-Header "Disk Space"

$scriptDrive = (Get-Item $PSScriptRoot).PSDrive
$freeGB = [math]::Round(($scriptDrive.Free / 1GB), 1)
$totalGB = [math]::Round((($scriptDrive.Free + $scriptDrive.Used) / 1GB), 1)

if ($freeGB -ge 5) {
    Write-CheckResult "Disk Space ($($scriptDrive.Name):)" "OK" "${freeGB} GB free of ${totalGB} GB"
} elseif ($freeGB -ge 2) {
    Write-CheckResult "Disk Space ($($scriptDrive.Name):)" "WARN" "${freeGB} GB free (5+ GB recommended for package operations)"
} else {
    Write-CheckResult "Disk Space ($($scriptDrive.Name):)" "FAIL" "Only ${freeGB} GB free. Large packages require 2+ GB working space." `
        "Free up disk space or change TempWorkingDir to a drive with more space"
}

# Check TEMP drive separately if different
$tempDrive = (Get-Item $env:TEMP -ErrorAction SilentlyContinue)
if ($tempDrive -and $tempDrive.PSDrive.Name -ne $scriptDrive.Name) {
    $tempFreeGB = [math]::Round(($tempDrive.PSDrive.Free / 1GB), 1)
    if ($tempFreeGB -ge 5) {
        Write-CheckResult "Temp Drive ($($tempDrive.PSDrive.Name):)" "OK" "${tempFreeGB} GB free"
    } else {
        Write-CheckResult "Temp Drive ($($tempDrive.PSDrive.Name):)" "WARN" "${tempFreeGB} GB free (used for package processing)"
    }
}

# ─────────────────────────────────────────────
#  6. Network / Connectivity (for deployment)
# ─────────────────────────────────────────────

Write-Header "Network Connectivity"

$endpoints = @(
    @{ Name = "Power Platform API"; Url = "https://api.powerplatform.com"; Required = $true },
    @{ Name = "Azure AD / Entra ID"; Url = "https://login.microsoftonline.com"; Required = $true },
    @{ Name = "NuGet (for PAC updates)"; Url = "https://api.nuget.org"; Required = $false }
)

foreach ($ep in $endpoints) {
    try {
        $response = Invoke-WebRequest -Uri $ep.Url -Method Head -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        Write-CheckResult $ep.Name "OK" "Reachable ($($ep.Url))"
    } catch {
        if ($ep.Required) {
            Write-CheckResult $ep.Name "WARN" "Cannot reach $($ep.Url)" `
                "Check firewall/proxy settings. Required for deployment."
        } else {
            Write-CheckResult $ep.Name "WARN" "Cannot reach $($ep.Url) (not critical)"
        }
    }
}

# ─────────────────────────────────────────────
#  7. Project Files (Development mode)
# ─────────────────────────────────────────────

if ($Mode -eq "Development") {
    Write-Header "Project Files"

    $projectFile = Join-Path $PSScriptRoot "src\DeployPortal\DeployPortal.csproj"
    if (Test-Path $projectFile) {
        Write-CheckResult "Project file" "OK" "src\DeployPortal\DeployPortal.csproj"
    } else {
        Write-CheckResult "Project file" "FAIL" "DeployPortal.csproj not found at expected path"
    }

    $solutionFile = Join-Path $PSScriptRoot "Project4.sln"
    if (Test-Path $solutionFile) {
        Write-CheckResult "Solution file" "OK" "Project4.sln"
    } else {
        Write-CheckResult "Solution file" "WARN" "Project4.sln not found"
    }

    # Template files
    $templateDir = Join-Path $PSScriptRoot "src\DeployPortal\Resources\UnifiedTemplate"
    $templateDll = Join-Path $templateDir "TemplatePackage.dll"
    if (Test-Path $templateDll) {
        $templateCount = (Get-ChildItem $templateDir -Recurse -File).Count
        Write-CheckResult "Unified Templates" "OK" "$templateCount files in Resources/UnifiedTemplate/"
    } else {
        Write-CheckResult "Unified Templates" "FAIL" "TemplatePackage.dll not found in Resources/UnifiedTemplate/" `
            "Built-in converter requires these template files"
    }

    # Try dotnet restore
    Write-Host ""
    Write-Host "    Verifying NuGet packages..." -ForegroundColor Gray
    $restoreResult = & dotnet restore $projectFile 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-CheckResult "NuGet Restore" "OK" "All packages restored successfully"
    } else {
        Write-CheckResult "NuGet Restore" "FAIL" "Package restore failed" `
            "Run: dotnet restore src\DeployPortal\DeployPortal.csproj"
    }
}

# ─────────────────────────────────────────────
#  8. Published App (Production mode)
# ─────────────────────────────────────────────

if ($Mode -eq "Production") {
    Write-Header "Published Application"

    $publishDir = Join-Path $PSScriptRoot "publish"
    $exePath = Join-Path $publishDir "DeployPortal.exe"

    if (Test-Path $exePath) {
        $exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
        Write-CheckResult "DeployPortal.exe" "OK" "Found (${exeSize} MB)"

        $templateDll = Join-Path $publishDir "Resources\UnifiedTemplate\TemplatePackage.dll"
        if (Test-Path $templateDll) {
            Write-CheckResult "Unified Templates" "OK" "Found in publish/Resources/UnifiedTemplate/"
        } else {
            Write-CheckResult "Unified Templates" "FAIL" "Missing from publish folder" `
                "Rebuild with: .\publish.ps1"
        }
    } else {
        Write-CheckResult "DeployPortal.exe" "FAIL" "Not found in publish/ folder" `
            "Build with: .\publish.ps1"
    }
}

# ─────────────────────────────────────────────
#  9. Port Availability
# ─────────────────────────────────────────────

Write-Header "Port Availability"

$defaultPort = 5000
try {
    $listeners = Get-NetTCPConnection -LocalPort $defaultPort -State Listen -ErrorAction SilentlyContinue
    if ($listeners) {
        $proc = Get-Process -Id ($listeners | Select-Object -First 1).OwningProcess -ErrorAction SilentlyContinue
        $procName = if ($proc) { $proc.ProcessName } else { "unknown" }
        Write-CheckResult "Port $defaultPort" "WARN" "Already in use by '$procName'" `
            "Use different port: DeployPortal.exe --urls `"http://localhost:5001`""
    } else {
        Write-CheckResult "Port $defaultPort" "OK" "Available"
    }
} catch {
    Write-CheckResult "Port $defaultPort" "OK" "Check skipped (cannot verify)"
}

# ─────────────────────────────────────────────
#  Summary
# ─────────────────────────────────────────────

Write-Host ""
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "    Summary" -ForegroundColor White
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""

$summaryColor = if ($script:FailedChecks -gt 0) { "Red" } elseif ($script:WarningChecks -gt 0) { "Yellow" } else { "Green" }

Write-Host "    Total checks:   $($script:TotalChecks)" -ForegroundColor White
Write-Host "    Passed:         $($script:PassedChecks)" -ForegroundColor Green
if ($script:WarningChecks -gt 0) {
    Write-Host "    Warnings:       $($script:WarningChecks)" -ForegroundColor Yellow
}
if ($script:FailedChecks -gt 0) {
    Write-Host "    Failed:         $($script:FailedChecks)" -ForegroundColor Red
}

Write-Host ""

if ($script:FailedChecks -gt 0) {
    Write-Host "    STATUS: ISSUES FOUND" -ForegroundColor Red
    Write-Host "    Fix the failed checks above before running DeployPortal." -ForegroundColor Red
    Write-Host ""
    Write-Host "    Quick fix commands:" -ForegroundColor Cyan

    if (-not $dotnetPath) {
        Write-Host "      # Install .NET 9 SDK:" -ForegroundColor Gray
        Write-Host "      winget install Microsoft.DotNet.SDK.9" -ForegroundColor White
        Write-Host "      # OR download from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor DarkGray
        Write-Host ""
    }
    if (-not $pacPath) {
        Write-Host "      # Install PAC CLI:" -ForegroundColor Gray
        Write-Host "      dotnet tool install --global Microsoft.PowerApps.CLI.Tool" -ForegroundColor White
        Write-Host ""
    }
} elseif ($script:WarningChecks -gt 0) {
    Write-Host "    STATUS: READY (with warnings)" -ForegroundColor Yellow
    Write-Host "    DeployPortal can run, but review warnings above." -ForegroundColor Yellow
} else {
    Write-Host "    STATUS: ALL CLEAR!" -ForegroundColor Green
    Write-Host "    DeployPortal is ready to run." -ForegroundColor Green
}

Write-Host ""

# ─────────────────────────────────────────────
#  Next steps
# ─────────────────────────────────────────────

Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "    Next Steps" -ForegroundColor White
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""

if ($Mode -eq "Development") {
    Write-Host "    1. Start the app:        .\run.ps1" -ForegroundColor White
    Write-Host "    2. Open in browser:      http://localhost:5076" -ForegroundColor White
    Write-Host "    3. Configure Settings:   /settings page" -ForegroundColor White
    Write-Host "    4. Add environments:     /environments page" -ForegroundColor White
    Write-Host ""
    Write-Host "    To publish for distribution:" -ForegroundColor Gray
    Write-Host "      .\publish.ps1" -ForegroundColor White
} else {
    Write-Host "    1. Run:    start.cmd  (or DeployPortal.exe)" -ForegroundColor White
    Write-Host "    2. Open:   http://localhost:5000" -ForegroundColor White
    Write-Host "    3. Setup:  Go to Settings and configure paths" -ForegroundColor White
    Write-Host "    4. Auth:   Run Setup-ServicePrincipal.ps1 or" -ForegroundColor White
    Write-Host "               follow Setup-ServicePrincipal-Manual.md" -ForegroundColor White
}

Write-Host ""

# ─────────────────────────────────────────────
#  Useful Links
# ─────────────────────────────────────────────

Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host "    Download Links" -ForegroundColor White
Write-Host "  =============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "    .NET 9 SDK:         https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Gray
Write-Host "    PAC CLI docs:       https://learn.microsoft.com/power-platform/developer/cli/introduction" -ForegroundColor Gray
Write-Host "    PowerShell 7:       https://aka.ms/install-powershell" -ForegroundColor Gray
Write-Host "    VS Code:            https://code.visualstudio.com/" -ForegroundColor Gray
Write-Host "    D365FO Dev Tools:   https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/development-tools-overview" -ForegroundColor Gray
Write-Host ""
