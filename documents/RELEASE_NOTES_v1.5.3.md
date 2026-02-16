# D365FO Deploy Portal — Release Notes v1.5.3

**Release Date:** February 2026  
**Type:** Feature Release — Deployment queue settings, delay between starts

---

## New Features & Improvements

### Deployment queue: configurable concurrency

- **Max concurrent deployments** — configurable in **Settings** (Deployment queue section) and stored in the **database** (table `AppSettings`). Default is **2** (up to two deployments can run at once: one package to one environment each).
- Range: 1–20. The change takes effect after restarting the application.

### Delay between deployment starts

- A **30-second** delay is enforced between deployment **starts**: the first deployment starts immediately, each next one starts no sooner than 30 seconds after the previous start. Reduces peak load when many deployments are queued.

### Settings & database

- Added **`AppSetting`** entity and **`AppSettings`** table in the database (key-value). On first run of v1.5.3 the table is created automatically (migration on startup).
- Other settings remain in `usersettings.json`; only **MaxConcurrentDeployments** is stored in the database.

### Tests

- **SettingsService_MaxConcurrentDeployments_StoredInDatabase** — verifies read/write of the value from the database.
- **DeploymentOrchestrator_DelayBetweenStarts_IsThirtySeconds** — verifies the 30-second delay constant.
- Tests that create `SettingsService` were updated for the new constructor (passing `IDbContextFactory<AppDbContext>`).

---

## Technical Details

### Modified / New Files

- `src/DeployPortal/Data/AppSetting.cs` (new)
- `src/DeployPortal/Data/AppDbContext.cs` — `DbSet<AppSetting>`, table creation on startup
- `src/DeployPortal/Services/ISettingsService.cs` — property `MaxConcurrentDeployments`
- `src/DeployPortal/Services/SettingsService.cs` — read/write from database, constants 1–20, default 2
- `src/DeployPortal/Services/DeploymentOrchestrator.cs` — dependency on `ISettingsService`, 30-second delay between starts, constant `DelayBetweenStartsSeconds`
- `src/DeployPortal/Components/Pages/Settings.razor` — Deployment queue section, Max concurrent deployments field
- `src/DeployPortal/Program.cs` — creation of `AppSettings` table
- `src/DeployPortal.Tests/UnitTests.cs` — MaxConcurrentDeployments test, passing `_dbFactory` to SettingsService
- `src/DeployPortal.Tests/Deployment/DeploymentServicesUnitTests.cs` — helper `CreateSettingsService`, delay test, `PooledDbContextFactory`

**Project:** `src/DeployPortal/DeployPortal.csproj` — version 1.5.3

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.5.3
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.5.3
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Download `DeployPortal-1.5.3-win-x64.zip` from the [Releases](https://github.com/vglu/D365FODeployPortal/releases) page.
2. Extract and run `start.cmd` or `DeployPortal.exe`.

---

## What's Next?

- [v1.5.4 Release Notes](RELEASE_NOTES_v1.5.4.md) — PAC auth isolation, Friendly Name options, deploy status fix
- [v1.5.0 Release Notes](RELEASE_NOTES_v1.5.0.md) — SOLID refactoring, tests, E2E
- [v1.4.0 Release Notes](RELEASE_NOTES_v1.4.0.md) — Deployment History Archive

---

## Support

For questions and issues, please open an [Issue](https://github.com/vglu/D365FODeployPortal/issues) in the repository.
