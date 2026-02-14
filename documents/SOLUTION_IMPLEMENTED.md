# Solution Implemented: PAC Auth Isolation

## What Was Done

Implemented **Option 3: Isolated PAC_AUTH_PROFILE_DIR** to guarantee connection to the correct environment when a Service Principal has access to multiple environments.

---

## How It Works

### Before changes (problematic case):

```
PAC CLI uses global auth profile storage:
%USERPROFILE%\.pac\auth\

Deployment #1: pac auth create --environment cst-hfx-tst-07
               ↓
           creates profile1 in global folder
               ↓
           but SP has access to multiple envs
               ↓
           PAC CLI picks c365afspmunified ❌
               ↓
           deployment goes to WRONG environment!
```

### After changes (solution with dual validation):

```
Deployment #1 (cst-hfx-tst-07):
   isolatedAuthDir = C:\Temp\DeployPortal\pac_auth_1_abc123...
   PAC_AUTH_PROFILE_DIRECTORY = isolatedAuthDir
   ↓
   pac auth create → stored ONLY in pac_auth_1_abc123
   ↓
   [CHECK 1] pac auth who → validation (verify URL is correct)
   ↓ if wrong → Exception before deploy
   ↓ if correct → continue
   ↓
   pac package deploy → uses auth ONLY from pac_auth_1_abc123
   ↓
   [CHECK 2] parse log file → verify "Deployment Target Organization Uri"
   ↓ if wrong → Exception, deployment Failed
   ↓ if correct → Success
   ↓
   deployment completed on CORRECT environment ✅✅
   ↓
   folder removed

Deployment #2 (in parallel to cst-hfx-tst-05):
   isolatedAuthDir = C:\Temp\DeployPortal\pac_auth_2_xyz789...
   PAC_AUTH_PROFILE_DIRECTORY = isolatedAuthDir
   ↓
   fully isolated from Deployment #1! ✅
```

---

## Modified Files

### 1. `src/DeployPortal/Services/DeployService.cs`

**Added:**
- ✅ Parameter `isolatedAuthDir` in `DeployPackageAsync`
- ✅ Setting `PAC_AUTH_PROFILE_DIRECTORY` for all PAC CLI calls
- ✅ New method `RunPacCommandWithOutputAsync` to capture output
- ✅ Validation via `pac auth who` after authentication
- ✅ Automatic cleanup in `finally` block
- ✅ Detailed isolation and validation logging

**Key code:**
```csharp
// Each PAC CLI process gets a unique folder
psi.EnvironmentVariables["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir;

// Verify we connected to the correct env
if (!whoOutput.Contains(environment.Url, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"PAC authentication verification FAILED! Expected: {environment.Url}");
}
```

### 2. `src/DeployPortal/Services/DeploymentOrchestrator.cs`

**Added:**
- ✅ Creation of unique `isolatedAuthDir` per deployment
- ✅ Passing `isolatedAuthDir` to `DeployPackageAsync`

**Code:**
```csharp
var isolatedAuthDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");

await deployService.DeployPackageAsync(
    deployment.Environment,
    deployDir,
    logFilePath,
    isolatedAuthDir,  // ← isolated folder
    msg => Log(msg));
```

---

## Benefits

### Full isolation
Each deployment uses its own auth profile. No global state.

### Parallel deployments
You can deploy to multiple environments at once without conflicts:
```
Deployment #1 → Cst-hfx-tst-07 (parallel)
Deployment #2 → Contoso-Test-02 (parallel)
Deployment #3 → Infra-tst-01 (parallel)
```

### Two-level validation

**Level 1 (PRE-DEPLOY): `pac auth who` check**
- Runs **before** deployment starts
- If wrong env → Exception **without applying package**
- Fast check (< 1 second)

**Level 2 (POST-DEPLOY): Log file check**
- Runs **after** deployment
- Parses "Deployment Target Organization Uri" from log
- Final confirmation that package was applied to correct env
- If wrong → deployment marked Failed with detailed message

### Automatic cleanup
`pac_auth_*` folder is removed after deployment (even on Exception).

### Independence from global state
It does not matter:
- Who ran `pac auth` manually and when
- Whether old auth profiles exist in `%USERPROFILE%\.pac\auth`
- Which auth was active before the run

---

## What You’ll See in Logs

On the next deployment you’ll see **two-level validation**:

```
[Isolation] Using dedicated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_4a5b6c7d8e9f...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...
Verifying connection (pac auth who)...
Connection verified.

[CHECK 1 - PRE-DEPLOY]
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com ✓

Starting deployment to Cst-hfx-tst-07...
Package: D:\Temp\...\TemplatePackage.dll
...
(deployment in progress...)
...
Deployment to Cst-hfx-tst-07 completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://cst-hfx-tst-07.crm.dynamics.com/XRMServices/...
[Post-Deploy Validation] ✓ Matches expected environment: cst-hfx-tst-07.crm.dynamics.com
[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment.

[Cleanup] Removed isolated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_4a5b6c7d8e9f...
```

### If something goes wrong

**Scenario 1: PAC auth connected to wrong env (CHECK 1 failed):**
```
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com
Connection verified.
[ERROR] PAC authentication verification FAILED! 
        Expected environment: cst-hfx-tst-07.crm.dynamics.com, 
        but 'pac auth who' returned different environment.

Deployment FAILED (before deploy — nothing applied) ✓
```

**Scenario 2: Deploy went to wrong env (CHECK 2 failed):**
```
Deployment to Cst-hfx-tst-07 completed.
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://c365afspmunified.crm.dynamics.com/...
[ERROR] ❌ POST-DEPLOYMENT VALIDATION FAILED! ❌
        Package was deployed to WRONG environment!
        Expected: cst-hfx-tst-07.crm.dynamics.com
        Actual: c365afspmunified.crm.dynamics.com

Deployment marked as FAILED (package already applied to wrong env) ⚠️
```

The second scenario is unlikely if the first check passed, but adds extra confidence.

---

## Testing

### Step 1: Run a deployment
1. Open the portal: http://localhost:5137/deploy
2. Select a package and environment "Cst-hfx-tst-07"
3. Start deployment

### Step 2: Check logs
1. In the portal UI, watch real-time logs
2. Find the line `[Isolation] Using dedicated PAC auth directory:`
3. Find the line `[Validation] Confirmed connected to correct environment:`

### Step 3: Check the final log file
```powershell
# After deployment completes
$logPath = "C:\Temp\DeployPortal\logs\deploy_X_YYYYMMDD_HHMMSS.log"
Select-String "Deployment Target Organization Uri" $logPath
```

You should see:
```
Deployment Target Organization Uri: https://cst-hfx-tst-07.crm.dynamics.com/...
```

### Step 4: Test parallel deployments
Run two deployments at the same time to different environments and confirm both succeed.

---

## Documentation

Full documentation is in:
- `PAC_AUTH_ISOLATION.md` — technical description of the solution

---

## Status

- ✅ Code implemented
- ✅ Build succeeds
- ✅ No linter errors
- ⏳ Pending testing on a real deployment

---

Implementation date: 2026-02-13  
Reason: Fix for deployments #1 and #2 going to the wrong environment
