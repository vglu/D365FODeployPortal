# D365FO Deploy Portal — Release Notes v1.8.3

**Release Date:** September 2026  
**Type:** Bug fix — PAC deploy via Unified zip + publish key safety + FO failure markers

---

## Bug Fixes

### PAC deploy: prefer Unified zip over bare `TemplatePackage.dll`

Even with the correct working directory, deploying a bare `TemplatePackage.dll` could still produce:

```text
Failed to Load the Import Configuration : Config File Missing
Selected Plugin is null
```

**Root cause:** Package Deployer sometimes loads the DLL without its sibling `PackageAssets` folder. When the same folder is zipped, PAC extracts DLL + `PackageAssets` together (e.g. `%TEMP%\xxxx.CPY\PackageAssets\ImportConfig.xml`) and the config loads reliably.

**Fix:**
- Before `pac package deploy`, zip the Unified output folder and pass that zip as `--package`
- Keep working directory on the Unified folder; use `--verbose`
- Pre-check `PackageAssets/ImportConfig.xml` and log PackageAssets file count

Verified end-to-end: Merged AL+PCM → Unified zip → deploy to Cst-hfx-tst-05 → `PACKAGE DEPLOYMENT PROCESS COMPLETED. Result:SUCCESS`.

### Keep failed deploy folders for inspection

On deployment failure, the temp `deploy_*` working folder is retained and logged (`[Debug] Keeping deploy folder...`) instead of always deleting it.

### Detect FO host / orchestration failures

Failure detector now also matches:

- `ExternalOrchestration`
- `Mismatched to Finance and Operations Application Host`
- `Failed to parse package`
- related FO module validation / config read failures

### Local `publish.ps1` no longer wipes DB / Data Protection keys

Cleaning `publish\` previously deleted `deploy-portal.db` and `DataProtection-Keys`, which caused:

- antiforgery cookie decrypt errors (`key was not found in the key ring`)
- broken Client Secret decryption for environments

**Fix:** preserve and restore `deploy-portal.db` (+ WAL/SHM) and `DataProtection-Keys` across publish.

### Packages Actions UI polish

Restored compact icon Actions (open folder / download / overflow) with a narrower sticky Actions column and safer Name/File width classes.

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```powershell
docker pull ghcr.io/vglu/d365fo-deploy-portal:v1.8.3
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```powershell
docker pull vglu/d365fo-deploy-portal:v1.8.3
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (self-contained)
Download `DeployPortal-1.8.3-win-x64.zip` from Releases, extract and run `start.cmd`.

If you keep a live `publish\` folder, prefer `scripts/publish.ps1` (now preserves DB/keys). After upgrading from a publish that already lost keys, clear site cookies once and re-enter Client Secrets if decrypt fails.

---

## Support

- Website: https://sims-service.com/
- Email: vhlu@sims-service.com
