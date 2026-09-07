# D365FO Deploy Portal — Release Notes v1.8.2

**Release Date:** September 2026  
**Type:** Bug fix — deploy false-success (ImportConfig) + Packages Actions UI

---

## Bug Fixes

### `Config File Missing` still marked as Success (critical)

Deployments could finish in a few seconds with:

```text
Failed to Load the Import Configuration : Config File Missing
Selected Plugin is null
Error: Failed to Load the Import Configuration : Config File Missing
```

…and the portal still showed **Success**.

**Root cause:** `pac package deploy` ran with working directory set to ModelUtil/app folder instead of the Unified package folder. Package Deployer looks for `PackageAssets/ImportConfig.xml` relative to that directory, so the config was “missing” even when conversion had created it correctly.

**Fix:**
- Run `pac package deploy` from the directory that contains `TemplatePackage.dll`
- Pre-check that `PackageAssets/ImportConfig.xml` exists before calling PAC
- Expand failure detection (`Config File Missing`, any PAC `Error:` line, `Selected Plugin is null`, etc.)
- Treat missing Organization Uri in the deploy log as failure (early abort), not a skip

### Packages page — Actions column “ran away”

Download / menu actions on the Active Packages table were pushed off-screen by long Name/File values, so the right side looked empty (only the license gavel column remained visible).

**Fix:**
- Sticky Actions column on the right
- Visible **Properties** and **Tools** controls
- Ellipsis truncation for long Name/File cells + horizontal scroll safety

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```powershell
docker pull ghcr.io/vglu/d365fo-deploy-portal:v1.8.2
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```powershell
docker pull vglu/d365fo-deploy-portal:v1.8.2
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (self-contained)
Download `DeployPortal-1.8.2-win-x64.zip` from Releases, extract and run `start.cmd`.

If you keep a live `publish\` folder with `deploy-portal.db`, back up the database before replacing binaries.

---

## Support

- Website: https://sims-service.com/
- Email: vhlu@sims-service.com
