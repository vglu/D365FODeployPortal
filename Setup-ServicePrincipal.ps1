#Requires -Modules Az.Accounts, Az.Resources, Microsoft.PowerApps.Administration.PowerShell

<#
.SYNOPSIS
    Creates a Service Principal for automated D365FO package deployment via PAC CLI.
.DESCRIPTION
    The script performs:
    1. Creates an App Registration in Entra ID (or uses an existing one)
    2. Creates a Client Secret (skipped in -AddEnvironment mode)
    3. Assigns Dataverse API permission with admin consent
    4. Registers an Application User in Power Platform environment(s)

    Two operating modes:
    - Full setup: creates SP + registers in all environments
    - Add environment: only registers an existing SP in a new environment
.NOTES
    Required permissions:
    - Global Administrator or Application Administrator in Entra ID
    - System Administrator in Power Platform environments

.EXAMPLE
    # Full setup — creates SP and registers in all default environments
    .\Setup-ServicePrincipal.ps1

.EXAMPLE
    # Full setup with a custom environment list
    .\Setup-ServicePrincipal.ps1 -Environments "env1.crm.dynamics.com","env2.crm.dynamics.com"

.EXAMPLE
    # Add a single new environment to an existing SP
    .\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com"

.EXAMPLE
    # Add multiple new environments to an existing SP
    .\Setup-ServicePrincipal.ps1 -AddEnvironment "env-a.crm.dynamics.com","env-b.crm.dynamics.com"

.EXAMPLE
    # Add an environment to an SP with a custom display name
    .\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com" -AppDisplayName "My-Custom-SP"
#>

[CmdletBinding(DefaultParameterSetName = "FullSetup")]
param(
    [Parameter()]
    [string]$AppDisplayName = "PAC-Deploy-Automation",

    [Parameter(ParameterSetName = "FullSetup")]
    [int]$SecretExpirationMonths = 24,

    [Parameter(ParameterSetName = "FullSetup")]
    [string[]]$Environments = @(
        # Replace with your actual Power Platform environment URLs
        # "your-env-01.crm.dynamics.com",
        # "your-env-02.crm.dynamics.com"
    ),

    [Parameter(Mandatory, ParameterSetName = "AddEnv")]
    [string[]]$AddEnvironment
)

$ErrorActionPreference = "Stop"

function Log {
    param([string]$Message, [string]$Color = "White")
    Write-Host "$(Get-Date -Format 'HH:mm:ss') - $Message" -ForegroundColor $Color
}

# ============================================================
# Determine operating mode
# ============================================================
$isAddEnvMode = $PSCmdlet.ParameterSetName -eq "AddEnv"

if ($isAddEnvMode) {
    Log "Mode: ADD ENVIRONMENT to existing Service Principal '$AppDisplayName'" "Cyan"
    Log "Environments to add: $($AddEnvironment -join ', ')" "Cyan"
} else {
    Log "Mode: FULL SETUP of Service Principal '$AppDisplayName'" "Cyan"
    Log "Environments: $($Environments -join ', ')" "Cyan"
}

Write-Host ""

# ============================================================
# PART 1: Authenticate to Azure
# ============================================================
Log "Signing in to Azure AD (browser will open)..." "Cyan"
Connect-AzAccount | Out-Null
$context = Get-AzContext
$tenantId = $context.Tenant.Id
Log "Tenant: $tenantId ($($context.Tenant.Name))" "Green"

# ============================================================
# PART 2: Create or find App Registration
# ============================================================
Log "Looking for App Registration: $AppDisplayName" "Cyan"

$existingApp = Get-AzADApplication -DisplayName $AppDisplayName -ErrorAction SilentlyContinue

if ($isAddEnvMode) {
    # In add-environment mode the SP must already exist
    if (-not $existingApp) {
        Write-Error "Application '$AppDisplayName' not found in Entra ID. Run full setup first (without -AddEnvironment)."
        exit 1
    }
    $app = $existingApp
    Log "Application found. AppId: $($app.AppId)" "Green"
} else {
    # Full setup — create if not exists
    if ($existingApp) {
        Log "Application '$AppDisplayName' already exists (AppId: $($existingApp.AppId)). Using existing." "Yellow"
        $app = $existingApp
    } else {
        $app = New-AzADApplication -DisplayName $AppDisplayName -SignInAudience "AzureADMyOrg"
        Log "Application created. AppId: $($app.AppId)" "Green"
    }
}

# ============================================================
# PART 3: Create Service Principal (if not exists)
# ============================================================
$sp = Get-AzADServicePrincipal -ApplicationId $app.AppId -ErrorAction SilentlyContinue
if (-not $sp) {
    if ($isAddEnvMode) {
        Write-Error "Service Principal for '$AppDisplayName' not found. Run full setup first."
        exit 1
    }
    Log "Creating Service Principal..." "Cyan"
    $sp = New-AzADServicePrincipal -ApplicationId $app.AppId
    Log "Service Principal created." "Green"
} else {
    Log "Service Principal already exists." $(if ($isAddEnvMode) { "Green" } else { "Yellow" })
}

# ============================================================
# PART 4: Create Client Secret (full setup mode only)
# ============================================================
$clientSecret = $null
if (-not $isAddEnvMode) {
    Log "Creating Client Secret (expires in $SecretExpirationMonths months)..." "Cyan"
    $endDate = (Get-Date).AddMonths($SecretExpirationMonths)
    $secret = New-AzADAppCredential -ApplicationId $app.AppId -EndDate $endDate

    $clientSecret = $secret.SecretText
    if (-not $clientSecret) {
        Write-Error "Failed to retrieve secret value. Try creating it manually in the portal."
        exit 1
    }
    Log "Client Secret created. Expires: $($endDate.ToString('yyyy-MM-dd'))" "Green"
} else {
    Log "Skipping Client Secret creation (add environment mode)." "Yellow"
}

# ============================================================
# PART 5: Assign Dataverse API Permission (full setup mode only)
# ============================================================
if (-not $isAddEnvMode) {
    Log "Assigning API Permission (Dataverse / user_impersonation)..." "Cyan"

    # Dataverse (Common Data Service) - well-known AppId
    $dataverseAppId = "00000007-0000-0000-c000-000000000000"
    # user_impersonation scope ID for Dataverse
    $userImpersonationId = "78ce3f0f-a1ce-49c2-8cde-64b5c0896db4"

    $resourceAccess = New-Object Microsoft.Azure.PowerShell.Cmdlets.Resources.MSGraph.Models.ApiV10.MicrosoftGraphResourceAccess
    $resourceAccess.Id = $userImpersonationId
    $resourceAccess.Type = "Scope"

    $requiredAccess = New-Object Microsoft.Azure.PowerShell.Cmdlets.Resources.MSGraph.Models.ApiV10.MicrosoftGraphRequiredResourceAccess
    $requiredAccess.ResourceAppId = $dataverseAppId
    $requiredAccess.ResourceAccess = @($resourceAccess)

    try {
        Update-AzADApplication -ApplicationId $app.AppId -RequiredResourceAccess @($requiredAccess)
        Log "API Permission added." "Green"
    } catch {
        Log "Failed to add API Permission automatically: $_" "Yellow"
        Log "Add manually: Azure Portal > App Registrations > $AppDisplayName > API Permissions > Dataverse > user_impersonation" "Yellow"
    }

    # Admin Consent
    Log "Attempting to grant Admin Consent..." "Cyan"
    try {
        $token = (Get-AzAccessToken -ResourceUrl "https://graph.microsoft.com").Token
        $headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
        $dataverseSp = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId eq '$dataverseAppId'" -Headers $headers

        if ($dataverseSp.value.Count -gt 0) {
            Log "Admin Consent: open the URL below in your browser to confirm:" "Yellow"
            $consentLink = "https://login.microsoftonline.com/$tenantId/adminconsent?client_id=$($app.AppId)"
            Log $consentLink "White"
            Start-Process $consentLink
            Read-Host "Press Enter after confirming Admin Consent in the browser"
            Log "Admin Consent granted." "Green"
        }
    } catch {
        Log "Admin Consent must be granted manually:" "Yellow"
        Log "Azure Portal > App Registrations > $AppDisplayName > API Permissions > Grant admin consent" "Yellow"
    }
} else {
    Log "Skipping API Permissions setup (add environment mode)." "Yellow"
}

# ============================================================
# PART 6: Register Application User in Power Platform
# ============================================================
$targetEnvironments = if ($isAddEnvMode) { $AddEnvironment } else { $Environments }

Log "Registering Application User in Power Platform environments..." "Cyan"

try {
    Add-PowerAppsAccount
    Log "Connected to Power Platform Admin." "Green"
} catch {
    Log "Error connecting to Power Platform: $_" "Red"
    Log "Application User must be registered manually via Power Platform Admin Center." "Yellow"
}

$successEnvs = @()
$failedEnvs = @()

foreach ($envUrl in $targetEnvironments) {
    Log "  Registering in $envUrl..." "Cyan"
    try {
        New-PowerAppManagementApp -ApplicationId $app.AppId -ErrorAction Stop
        Log "  [OK] $envUrl - Application User registered" "Green"
        $successEnvs += $envUrl
    } catch {
        if ($_.Exception.Message -like "*already exists*") {
            Log "  [SKIP] $envUrl - already registered" "Yellow"
            $successEnvs += $envUrl
        } else {
            Log "  [FAIL] $envUrl - error: $($_.Exception.Message)" "Red"
            Log "     Register manually: PPAC > Environment > Settings > Application Users" "Yellow"
            $failedEnvs += $envUrl
        }
    }
}

# ============================================================
# RESULT
# ============================================================
Write-Host ""
Write-Host "==============================================================" -ForegroundColor Green

if ($isAddEnvMode) {
    Write-Host "  ENVIRONMENT(S) ADDED TO SERVICE PRINCIPAL" -ForegroundColor Green
} else {
    Write-Host "  SERVICE PRINCIPAL CREATED SUCCESSFULLY" -ForegroundColor Green
}

Write-Host "==============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Application (Client) ID:  $($app.AppId)" -ForegroundColor White
Write-Host "  Directory (Tenant) ID:    $tenantId" -ForegroundColor White

if ($clientSecret) {
    Write-Host "  Client Secret:            $clientSecret" -ForegroundColor White
    Write-Host "  Secret expires:           $($endDate.ToString('yyyy-MM-dd'))" -ForegroundColor White
    Write-Host ""
    Write-Host "  IMPORTANT: Save the Client Secret — it will not be shown again!" -ForegroundColor Red
}

Write-Host ""
if ($successEnvs.Count -gt 0) {
    Write-Host "  Environments (OK):        $($successEnvs -join ', ')" -ForegroundColor Green
}
if ($failedEnvs.Count -gt 0) {
    Write-Host "  Environments (FAIL):      $($failedEnvs -join ', ')" -ForegroundColor Red
}
Write-Host "==============================================================" -ForegroundColor Green

# Save metadata to file (without the secret)
$mode = if ($isAddEnvMode) { "AddEnvironment" } else { "FullSetup" }
$outputFile = Join-Path $PSScriptRoot "ServicePrincipal_${mode}_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
@"
Mode:                    $mode
Service Principal:       $AppDisplayName
Application (Client) ID: $($app.AppId)
Directory (Tenant) ID:   $tenantId
Client Secret:           $(if ($clientSecret) { '*** SHOWN IN CONSOLE ***' } else { 'Not created (add environment mode)' })
Secret expires:          $(if ($clientSecret) { $endDate.ToString('yyyy-MM-dd') } else { 'N/A' })
Created:                 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Environments (OK):       $($successEnvs -join ', ')
Environments (FAIL):     $($failedEnvs -join ', ')
"@ | Out-File $outputFile -Encoding UTF8

Log "" "White"
Log "Metadata saved to: $outputFile" "Cyan"
Log "" "White"
Log "Verification (run on the target machine):" "White"
$testEnv = if ($isAddEnvMode) { $AddEnvironment[0] } else { $Environments[0] }
Log "  pac auth create --applicationId `"$($app.AppId)`" --clientSecret `"<SECRET>`" --tenant `"$tenantId`" --environment `"$testEnv`"" "White"
Log "  pac auth who" "White"
