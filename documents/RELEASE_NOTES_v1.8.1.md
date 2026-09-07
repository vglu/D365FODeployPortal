# D365FO Deploy Portal — Release Notes v1.8.1

**Release Date:** September 2026  
**Type:** Bug fix — false-success detection for failed FO package installs

---

## Bug Fixes

### False "Success" when Finance and Operations install failed (critical)

PAC CLI / Package Deployer can exit with code **0** while still reporting that the Finance and Operations application installation failed. Example log lines:

```text
RaiseFailEvent - update progress with fail event
Error: Installation failed for Finance and Operations application
```

Previously the portal treated this as a successful deployment because:
1. Exit code was 0
2. Post-deploy validation only checked that the **target organization URL** matched

**Fix:**
- New `PackageDeployFailureDetector` scans PAC stdout/stderr and the deploy log for known failure markers
- `PacDeploymentService` fails the run even when exit code is 0 if failure markers are present
- `PostDeployLogValidator` fails the deployment when the log contains install-failure evidence (before the org-URL check)

Deployments that hit this case are now marked **Failed** with the matching log line in the error message.

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```powershell
docker pull ghcr.io/vglu/d365fo-deploy-portal:v1.8.1
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```powershell
docker pull vglu/d365fo-deploy-portal:v1.8.1
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (self-contained)
Download `DeployPortal-1.8.1-win-x64.zip` from Releases, extract and run `start.cmd`.

---

## Support

- Website: https://sims-service.com/
- Email: vhlu@sims-service.com
