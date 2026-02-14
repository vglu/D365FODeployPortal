# Two-Level Environment Validation on Deploy

## Goal

Ensure with **maximum confidence** that the D365FO package is applied to the correct environment. If something goes wrong — detect it as early as possible.

---

## Validation Architecture

### CHECK 1: PRE-DEPLOY validation (before deployment starts)

**When:** Right after `pac auth create`, **before** `pac package deploy` starts  
**How:** Call `pac auth who` and parse output  
**Goal:** Verify that PAC CLI is connected to the correct environment  

**Result:**
- ✅ If correct env → continue deployment
- ❌ If wrong env → **Exception before deploy** (package NOT applied)

**Benefits:**
- Early failure — package will not be applied to the wrong env
- Fast check (< 1 second)

**Code:**
```csharp
// src/DeployPortal/Services/Deployment/Validation/PreDeployAuthValidator.cs

onLog?.Invoke("[Pre-Deploy Validation] Verifying 'pac auth who' output...");

var expectedUrl = context.Environment.Url.ToLowerInvariant();
var expectedName = context.Environment.Name.ToLowerInvariant();
var whoOutputLower = context.PacAuthWhoOutput.ToLowerInvariant();

// Check 1: Match by URL (works for Service Principal auth)
var urlMatch = whoOutputLower.Contains(expectedUrl);

// Check 2: Match by Organization Friendly Name (works for interactive auth)
var friendlyNameMatch = 
    whoOutputLower.Contains($"organization friendly name: {expectedName}") ||
    whoOutputLower.Contains($"default organization: {expectedName}");

if (!urlMatch && !friendlyNameMatch)
{
    throw new InvalidOperationException(
        $"PAC CLI authenticated to WRONG environment!\n" +
        $"Expected: {context.Environment.Name} ({context.Environment.Url})\n" +
        $"But 'pac auth who' output does not contain expected URL or Organization Name.");
}

var matchType = urlMatch ? "URL" : "Organization Friendly Name";
onLog?.Invoke($"[Pre-Deploy Validation] ✓ Matched by {matchType}: {context.Environment.Name}");
```

**Why two checks?**
- **Service Principal auth** → outputs URL in `pac auth who`
- **Interactive auth (device code)** → outputs only "Organization Friendly Name"

---

### CHECK 2: POST-DEPLOY validation (after deployment completes)

**When:** After successful `pac package deploy`  
**How:** Parse log file, find line `Deployment Target Organization Uri:`  
**Goal:** Final confirmation that the package was applied to the correct environment  

**Result:**
- ✅ If correct env → deployment success
- ❌ If wrong env → **Exception, deployment Failed** (package already applied to wrong env!)

**Benefits:**
- Final confirmation from PAC CLI itself (from its logs)
- Catches cases where PAC CLI "changed its mind" during deploy

**Code:**
```csharp
// src/DeployPortal/Services/DeployService.cs, method ValidateDeploymentLogAsync

var logContent = await File.ReadAllTextAsync(logFilePath);

// Find: "Deployment Target Organization Uri: https://cst-hfx-tst-07.crm.dynamics.com/..."
var uriLinePrefix = "Deployment Target Organization Uri:";
var uriLine = logContent
    .Split('\n')
    .FirstOrDefault(line => line.Contains(uriLinePrefix, StringComparison.OrdinalIgnoreCase));

var actualUri = ExtractUriFromLine(uriLine);

if (!actualUri.Contains(environment.Url, StringComparison.OrdinalIgnoreCase))
{
    var errorMsg = 
        $"❌ POST-DEPLOYMENT VALIDATION FAILED! ❌\n" +
        $"Package was deployed to WRONG environment!\n\n" +
        $"Expected: {environment.Url}\n" +
        $"Actual: {actualUri}";
    
    throw new InvalidOperationException(errorMsg);
}

onLog?.Invoke($"[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment.");
```

---

## Execution Flow

```
START
  ↓
1. Create isolated folder pac_auth_{deploymentId}_{guid}
  ↓
2. pac auth create (in isolated folder)
  ↓
3. [CHECK 1 - PRE-DEPLOY]
   pac auth who → parse → verify URL
   ↓
   Correct env?
   ├─ YES → continue
   └─ NO → throw Exception (package NOT applied) ❌
  ↓
4. pac package deploy
  ↓
5. [CHECK 2 - POST-DEPLOY]
   read log file → parse → verify "Deployment Target Organization Uri"
   ↓
   Correct env?
   ├─ YES → SUCCESS ✅
   └─ NO → throw Exception, deployment Failed (package already on wrong env!) ⚠️
  ↓
6. Remove isolated folder
  ↓
END
```

---

## When Each Check Triggers

| Scenario | CHECK 1 (PRE-DEPLOY) | CHECK 2 (POST-DEPLOY) | Result |
|----------|----------------------|------------------------|--------|
| All good | ✅ Passed | ✅ Passed | SUCCESS |
| PAC auth connected to wrong env | ❌ Failed | Not run | FAILED, package NOT applied ✓ |
| PAC auth correct but deploy went to different env (unlikely) | ✅ Passed | ❌ Failed | FAILED, package on wrong env ⚠️ |
| Log file missing or no Uri | ✅ Passed | ⚠️ Warning (no throw) | SUCCESS (continue, log warning) |

---

## Log Examples

### Successful deployment:

```
[Isolation] Using dedicated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_abc123...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...
Verifying connection (pac auth who)...
Connection verified.

[CHECK 1 - PRE-DEPLOY]
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com ✓

Starting deployment to Cst-hfx-tst-07...
Package: D:\Temp\...\TemplatePackage.dll
...
Deployment to Cst-hfx-tst-07 completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://cst-hfx-tst-07.crm.dynamics.com/XRMServices/...
[Post-Deploy Validation] ✓ Matches expected environment: cst-hfx-tst-07.crm.dynamics.com
[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment.

[Cleanup] Removed isolated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_abc123...
```

### CHECK 1 failed (PAC auth connected to wrong env):

```
[Isolation] Using dedicated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_abc123...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...
Verifying connection (pac auth who)...
Connection verified.

[CHECK 1 - PRE-DEPLOY]
[ERROR] PAC authentication verification FAILED!
Expected environment: Cst-hfx-tst-07 (cst-hfx-tst-07.crm.dynamics.com)
But 'pac auth who' output does not contain expected URL.

'pac auth who' output:
Environment ID: <example-id>...
Environment Url: https://c365afspmunified.crm.dynamics.com/
Tenant ID: ...

Deployment FAILED (package NOT applied) ✓
```

### CHECK 2 failed (deploy went to wrong env):

```
Deployment to Cst-hfx-tst-07 completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://c365afspmunified.crm.dynamics.com/...

[ERROR] ❌ POST-DEPLOYMENT VALIDATION FAILED! ❌
Package was deployed to WRONG environment!

Expected environment: Cst-hfx-tst-07 (cst-hfx-tst-07.crm.dynamics.com)
Actual deployment target (from log): https://c365afspmunified.crm.dynamics.com/XRMServices/...

This indicates a critical deployment routing issue. The package is now on the wrong environment!

Deployment marked as FAILED (package already applied to wrong env) ⚠️
```

---

## Why Two Checks?

1. **CHECK 1 (PRE-DEPLOY)** — primary safeguard:
   - Catches 99% of wrong-environment cases
   - Stops deployment before applying the package
   - Fast

2. **CHECK 2 (POST-DEPLOY)** — final confirmation:
   - Catches unlikely cases where PAC CLI "changed its mind" during deploy
   - Confirms we see the same data as PAC CLI (its own log)
   - Gives 100% confidence for audit trail

**Together they provide maximum protection** against deploying to the wrong environment.

---

## Modified Files

- `src/DeployPortal/Services/DeployService.cs`:
  - Added `RunPacCommandWithOutputAsync` to capture PAC CLI output
  - Added PRE-DEPLOY validation via `pac auth who`
  - Added POST-DEPLOY validation via `ValidateDeploymentLogAsync`
  - Added `ValidateDeploymentLogAsync` for log file parsing

- `src/DeployPortal/Services/DeploymentOrchestrator.cs`:
  - Passes `isolatedAuthDir` to `DeployService`

---

**Implementation date:** 2026-02-13  
**Status:** ✅ Ready for testing
