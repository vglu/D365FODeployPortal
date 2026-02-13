# 📦 D365FO Deploy Portal — Release Notes v1.3.3

**Release Date:** February 13, 2026  
**Type:** UX Improvement Release

---

## ✨ UX Improvements

### Package Deletion: Proactive User-Friendly Messaging

**What Changed:**
Enhanced the package deletion flow with **proactive validation** and **informative dialogs** instead of showing errors after deletion fails.

**Before (v1.3.2):**
- User clicks "Delete"
- System tries to delete
- Database throws FOREIGN KEY error
- Error shown in Snackbar (not very visible)

**Now (v1.3.3):**
- User clicks "Delete"
- **System checks usage FIRST** (before attempting deletion)
- If package is used → shows **detailed informative dialog**:
  ```
  ╔═══════════════════════════════════╗
  ║   Cannot Delete Package           ║
  ╠═══════════════════════════════════╣
  ║ This package is used in 3         ║
  ║ deployments across these          ║
  ║ environments:                     ║
  ║                                   ║
  ║ CST-HFX-TST-07, Prod-Env, UAT-01  ║
  ║                                   ║
  ║ You must delete all related       ║
  ║ deployments before you can        ║
  ║ delete this package.              ║
  ╚═══════════════════════════════════╝
  ```
- If package is NOT used → shows confirmation dialog
- Only then proceeds with deletion

**Benefits:**
- ✅ **Proactive validation** — checks before attempting deletion
- ✅ **Clear information** — shows exactly how many deployments use the package
- ✅ **Environment names** — lists which environments are affected
- ✅ **Actionable guidance** — tells user what to do (delete deployments first)
- ✅ **Better visibility** — uses modal dialog instead of Snackbar
- ✅ **No errors** — user never sees database errors

---

## 🔧 Technical Implementation

### New Method in PackageService

**File:** `src/DeployPortal/Services/PackageService.cs`

Added `GetPackageUsageAsync()` method:
```csharp
/// <summary>
/// Checks if a package is used in any deployments and returns usage information.
/// </summary>
public async Task<(int DeploymentsCount, List<string> EnvironmentNames)> GetPackageUsageAsync(int packageId)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    
    var deployments = await db.Deployments
        .Where(d => d.PackageId == packageId)
        .Include(d => d.Environment)
        .ToListAsync();

    var envNames = deployments
        .Select(d => d.Environment?.Name ?? "Unknown")
        .Distinct()
        .OrderBy(n => n)
        .ToList();

    return (deployments.Count, envNames);
}
```

### Enhanced UI Logic

**File:** `src/DeployPortal/Components/Pages/Packages.razor`

```csharp
private async Task DeletePackage(Package pkg)
{
    // First, check if package is used in deployments
    var (deploymentsCount, envNames) = await PkgService.GetPackageUsageAsync(pkg.Id);

    if (deploymentsCount > 0)
    {
        // Show informative message about package usage
        var envList = string.Join(", ", envNames.Take(5));
        if (envNames.Count > 5)
            envList += $" and {envNames.Count - 5} more";

        var message = deploymentsCount == 1
            ? $"This package is used in 1 deployment (environment: {envList}).\n\n" +
              "You must delete the deployment before you can delete this package."
            : $"This package is used in {deploymentsCount} deployments across these environments:\n{envList}\n\n" +
              "You must delete all related deployments before you can delete this package.";

        await DialogService.ShowMessageBox("Cannot Delete Package", message, yesText: "OK");
        return;
    }

    // Package is not used, proceed with confirmation...
}
```

**Features:**
- Shows up to 5 environment names, indicates if there are more
- Handles singular vs plural ("1 deployment" vs "3 deployments")
- Provides clear guidance on what to do next

---

## 📄 Modified Files

- `src/DeployPortal/Services/PackageService.cs` — added `GetPackageUsageAsync()` method
- `src/DeployPortal/Components/Pages/Packages.razor` — enhanced deletion flow with proactive checks
- `src/DeployPortal/DeployPortal.csproj` — version bump to 1.3.3

---

## 📦 Installation & Upgrade

### Docker Users (recommended)
```bash
docker pull vglu/d365fo-deploy-portal:1.3.3

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
  vglu/d365fo-deploy-portal:1.3.3
```

### Manual Build
```bash
cd d:\Projects\D365FODeployPortal
git pull
dotnet restore
dotnet publish src/DeployPortal/DeployPortal.csproj -c Release -o ./publish
```

---

## 🚀 What's Next?

This is a UX improvement release. For previous changes, see:
- [v1.3.2 Release Notes](RELEASE_NOTES_v1.3.2.md) — FOREIGN KEY constraint fix
- [v1.3.1 Release Notes](RELEASE_NOTES_v1.3.1.md) — Interactive auth validation fix
- [v1.3.0 Release Notes](RELEASE_NOTES_v1.3.0.md) — Major SOLID refactoring

---

## 📞 Support

If you encounter any issues, please:
1. Check the [documentation](docs/)
2. Review logs in `C:\Temp\DeployPortal\logs` (Windows) or `/tmp/DeployPortal/logs` (Docker)
3. Contact support: vhlushchenko@sisn.com
