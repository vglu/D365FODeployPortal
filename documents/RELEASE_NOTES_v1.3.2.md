# 📦 D365FO Deploy Portal — Release Notes v1.3.2

**Release Date:** February 13, 2026  
**Type:** Bugfix Release

---

## 🐛 Bug Fixes

### Package Deletion: Fixed FOREIGN KEY Constraint Error

**Problem:**
When attempting to delete a package that was used in deployments, the application crashed with:

```
SQLite Error 19: 'FOREIGN KEY constraint failed'
```

This happened because the database protects referential integrity — you cannot delete a package that is referenced by deployment records.

**Root Cause:**
The `PackageService.DeleteAsync()` method did not check if the package was used in any deployments before attempting deletion, causing a database constraint violation.

**Solution:**
Enhanced package deletion logic with the following improvements:

1. **Pre-deletion validation** — checks if package is used in deployments
2. **User-friendly error message** — shows how many deployments use the package
3. **Graceful file deletion** — continues with DB deletion even if physical file deletion fails
4. **UI error handling** — displays clear error messages in the UI instead of crashing

**Code Changes:**

**File:** `src/DeployPortal/Services/PackageService.cs`
```csharp
// Check if package is used in any deployments
var deploymentsCount = await db.Deployments.CountAsync(d => d.PackageId == id);
if (deploymentsCount > 0)
{
    throw new InvalidOperationException(
        $"Cannot delete package '{package.Name}' because it is used in {deploymentsCount} deployment(s). " +
        $"Please delete all related deployments first.");
}
```

**File:** `src/DeployPortal/Components/Pages/Packages.razor`
```csharp
try
{
    await PkgService.DeleteAsync(pkg.Id);
    Snackbar.Add("Package deleted.", Severity.Success);
    await LoadData();
}
catch (InvalidOperationException ex)
{
    // Show user-friendly error message for FOREIGN KEY constraint
    Snackbar.Add(ex.Message, Severity.Error);
}
```

**Impact:**
- ✅ No more crashes when deleting packages
- ✅ Clear error message explaining why deletion failed
- ✅ User knows exactly what to do (delete deployments first)
- ✅ Graceful handling of file system errors

**User Experience:**
When you try to delete a package that's used in deployments, you'll now see:

```
❌ Cannot delete package 'MyPackage.zip' because it is used in 3 deployment(s). 
   Please delete all related deployments first.
```

---

## 📄 Modified Files

- `src/DeployPortal/Services/PackageService.cs` — added deployment count check
- `src/DeployPortal/Components/Pages/Packages.razor` — added error handling
- `src/DeployPortal/DeployPortal.csproj` — version bump to 1.3.2

---

## 📦 Installation & Upgrade

### Docker Users (recommended)
```bash
docker pull vglu/d365fo-deploy-portal:1.3.2

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
  vglu/d365fo-deploy-portal:1.3.2
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

This is a bugfix release. For previous changes, see:
- [v1.3.1 Release Notes](RELEASE_NOTES_v1.3.1.md) — Interactive auth validation fix
- [v1.3.0 Release Notes](RELEASE_NOTES_v1.3.0.md) — Major SOLID refactoring & new features

---

## 📞 Support

If you encounter any issues, please:
1. Check the [documentation](.)
2. Review logs in `C:\Temp\DeployPortal\logs` (Windows) or `/tmp/DeployPortal/logs` (Docker)
3. Contact support: [vhlu@sims-service.com](mailto:vhlu@sims-service.com) — [Sims Tech](https://sims-service.com/)
