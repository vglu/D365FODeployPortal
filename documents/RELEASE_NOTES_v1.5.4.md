# D365FO Deploy Portal — Release Notes v1.5.4

**Release Date:** February 2026  
**Type:** Feature & Bugfix — PAC auth isolation, Friendly Name options, deploy status fix

---

## New Features & Improvements

### PAC CLI auth isolation (deploy & Friendly Name)

- **Isolated auth profile directory** — Each deployment and each “Pull Friendly Name” / import flow uses a dedicated `PAC_AUTH_PROFILE_DIRECTORY` so parallel runs do not share credentials.
- **Named profiles** — All `pac auth create` calls use `--name "Deploy_<env>"` for clear separation.
- Applied in: **Deploy**, **Refresh Organization Friendly Name** (environment edit), and **Fill Friendly Name** (import). Same behavior for native Windows and Docker.

### Friendly Name: optional validation & manual pull

- **No auto-fill on import** — Organization Friendly Name is no longer filled automatically during “Import from Script Output” (avoids wrong names when PAC uses shared credentials).
- **Pull Friendly Name** — In the environment edit dialog, a **Pull Friendly Name** button fetches and saves the current environment’s friendly name on demand.
- **Setting: “Additionally verify Friendly Name on deploy”** — When enabled (Settings → Deployment safety), pre- and post-deploy checks use the stored Friendly Name; when disabled, Friendly Name validation is skipped.

### Deploy status fix

- **Auth failures no longer reported as Success** — If `pac auth create` fails with messages like “Could not connect to the Dataverse organization”, “The user is not a member of the organization”, or “invalid status code 'Forbidden'”, the deployment is now correctly marked as **Failed** (PAC CLI may still exit 0 in these cases; we parse stdout/stderr).

### Service Principal setup (docs)

- **Part 4** of the Service Principal setup guide (Environments page) now states clearly that the app must be added as an **Application User** in each target Power Platform environment (Power Platform Admin Center), with **System Administrator** role, to avoid Forbidden errors.

---

## Technical Details

### Modified / New Files

- `PacAuthService.cs` — `PAC_AUTH_PROFILE_DIRECTORY`, `--name`, post-auth output parsing for connection/Forbidden errors
- `PacAuthWhoParser.cs` — (new) parsing of `pac auth who` output
- `EnvironmentService.cs` — `RefreshOrganizationFriendlyNameAsync`, no auto-fill on import, isolated dir per env
- `Environments.razor` — Pull Friendly Name button, import without Friendly Name fill
- `Settings.razor` / `SettingsService.cs` / `ISettingsService.cs` — “VerifyOrganizationFriendlyNameOnDeploy”
- `PreDeployAuthValidator.cs` — runs only when setting is enabled
- `DeployService.cs` / `DeploymentContext.cs` — pass-through of verify setting
- `DeploymentServicesUnitTests.cs` — tests for new behavior

**Project:** `src/DeployPortal/DeployPortal.csproj` — version 1.5.4

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.5.4
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.5.4
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Download `DeployPortal-1.5.4-win-x64.zip` from the [Releases](https://github.com/vglu/D365FODeployPortal/releases) page.
2. Extract and run `start.cmd` or `DeployPortal.exe`.

---

## What's Next?

- [v1.5.3 Release Notes](RELEASE_NOTES_v1.5.3.md) — Deployment queue settings (MaxConcurrentDeployments, delay)
- [v1.5.0 Release Notes](RELEASE_NOTES_v1.5.0.md) — SOLID refactoring, tests, E2E
- [v1.4.0 Release Notes](RELEASE_NOTES_v1.4.0.md) — Deployment History Archive

---

## Support

For questions and issues, please open an [Issue](https://github.com/vglu/D365FODeployPortal/issues) in the repository.
