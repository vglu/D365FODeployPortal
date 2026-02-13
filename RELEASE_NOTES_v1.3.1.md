# 📦 D365FO Deploy Portal — Release Notes v1.3.1

**Release Date:** February 13, 2026  
**Type:** Bugfix Release

---

## 🐛 Bug Fixes

### Pre-Deployment Validation: Fixed False Positive for Interactive Authentication

**Problem:**
When using **Interactive Authentication (device code flow)**, the pre-deployment validator failed with a false positive error:

```
❌ PRE-DEPLOYMENT VALIDATION FAILED! ❌
PAC CLI authenticated to WRONG environment!
Expected environment: Cst-hfx-tst-07 (cst-hfx-tst-07.crm.dynamics.com)
But 'pac auth who' output does not contain expected URL.
```

**Root Cause:**
The validator only checked for the environment **URL** in the `pac auth who` output. However, for **interactive authentication**, the output does NOT contain the URL (e.g., `cst-hfx-tst-07.crm.dynamics.com`), but instead contains:

```
Organization Friendly Name: CST-HFX-TST-07
Organization Id: ef7d39e4-66d2-f011-8729-000d3a33a003
```

**Solution:**
Enhanced `PreDeployAuthValidator` to support **two validation methods**:

1. **Match by URL** — for Service Principal authentication (output contains URL)
2. **Match by Organization Friendly Name** — for Interactive authentication (output contains name)

The validator now passes if **either** method matches.

**Code Changes:**
- **File:** `src/DeployPortal/Services/Deployment/Validation/PreDeployAuthValidator.cs`
- **Changes:**
  - Added `friendlyNameMatch` check (searches for "Organization Friendly Name: {name}" in output)
  - Updated error message to mention both URL and Name
  - Added logging to indicate which match type was used

**Test Coverage:**
- Added new unit test: `PreDeployAuthValidator_ValidateAsync_Passes_WhenOrganizationFriendlyNameMatches`
- Updated test: `PreDeployAuthValidator_ValidateAsync_Throws_WhenNeitherUrlNorNameMatches`
- All 14 unit tests pass ✅

**Impact:**
- ✅ Interactive authentication (device code) now works correctly
- ✅ Service Principal authentication remains fully functional
- ✅ False positive errors eliminated

---

## 📄 Documentation

Updated documentation to reflect the new validation logic:
- **File:** `docs/ДВУХУРОВНЕВАЯ_ВАЛИДАЦИЯ.md`
- **Section:** CHECK 1: PRE-DEPLOY валидация

---

## 🔧 Technical Details

### Modified Files
- `src/DeployPortal/Services/Deployment/Validation/PreDeployAuthValidator.cs` — enhanced validation logic
- `src/DeployPortal.Tests/Deployment/DeploymentServicesUnitTests.cs` — added/updated tests
- `src/DeployPortal/DeployPortal.csproj` — version bump to 1.3.1
- `docs/ДВУХУРОВНЕВАЯ_ВАЛИДАЦИЯ.md` — documentation update

### Docker Images
- **Published to Docker Hub:**
  - `vglu/d365fo-deploy-portal:1.3.1`
  - `vglu/d365fo-deploy-portal:latest` (updated to 1.3.1)

---

## 📦 Installation & Upgrade

### Docker Users (recommended)
```bash
docker pull vglu/d365fo-deploy-portal:1.3.1

# Stop old container
docker stop deploy-portal
docker rm deploy-portal

# Start new container
docker run -d \
  --name deploy-portal \
  -p 8080:8080 \
  -v deploy-portal-data:/app/data \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e PAC_DISABLE_TELEMETRY=true \
  vglu/d365fo-deploy-portal:1.3.1
```

### Manual Build
```bash
cd d:\Projects\D365FODeployPortal
git pull
dotnet restore
dotnet publish src/DeployPortal/DeployPortal.csproj -c Release -o ./publish
```

---

## 🧪 Testing

All unit tests pass:
```
Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14
```

---

## 🚀 What's Next?

This is a bugfix release. For new features, see [v1.3.0 Release Notes](RELEASE_NOTES_v1.3.0.md).

---

## 📞 Support

If you encounter any issues, please:
1. Check the [documentation](docs/)
2. Review logs in `C:\Temp\DeployPortal\logs` (Windows) or `/tmp/DeployPortal/logs` (Docker)
3. Contact support: vhlushchenko@sisn.com
