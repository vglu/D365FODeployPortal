# D365FO Deploy Portal — Release Notes v1.8.0

**Release Date:** March 2026  
**Type:** Infrastructure — Windows Docker containers, deployment fix

---

## Breaking Change

### Docker image switched to Windows containers

The Docker image now uses **Windows Server Core LTSC 2022** instead of Linux. This was required because `pac package deploy` — the command used to deploy packages to Power Platform environments — is **only available in the Windows MSI distribution** of PAC CLI. The cross-platform (Linux) builds of PAC CLI have never included this command.

The previous Linux Dockerfile is preserved as `Dockerfile.linux` for users who only need package conversion (no deployment).

---

## Bug Fixes

### `pac package deploy` — command not found (critical)

Deployments from Docker containers failed with:
```
Error: The command 'deploy' is not understood in this context.
Usage: pac package [init] [add-external-package] [add-solution] [add-reference]
```
**Root cause:** `pac package deploy` was never available in the cross-platform PAC CLI NuGet packages (`Microsoft.PowerApps.CLI.Core.linux-x64`). It only exists in the Windows MSI installation.

**Fix:** Switched Docker image to Windows Server Core and install PAC CLI via MSI, which includes the full command set. The Dockerfile verifies `pac package deploy --help` during build to catch install failures early.

### Version display fixed

The portal UI now correctly shows `v1.8.0` (was displaying `v1.5.5` due to stale cached build).

---

## Infrastructure Changes

- **Dockerfile** — Windows Server Core LTSC 2022 multi-stage build with PAC CLI MSI + Azure CLI MSI
- **Dockerfile.linux** — Previous Linux Dockerfile preserved for convert-only use (no `pac package deploy`)
- **docker-compose.yml** — Updated for Windows containers: long-form volume syntax, Windows paths, PowerShell healthcheck
- **release.yml** — CI/CD pipeline: Docker build/push jobs moved from `ubuntu-latest` to `windows-latest`, using direct `docker build`/`docker push` (buildx not used for Windows containers)

---

## Migration Guide

### 1. Switch Docker Desktop to Windows containers

Right-click Docker Desktop tray icon → **"Switch to Windows containers..."**. All running Linux containers will stop.

### 2. Remove old Linux volumes

The existing Linux volumes are not compatible with Windows containers. Back up any data you need (database, usersettings.json), then remove them:

```powershell
docker volume rm deploy-portal-data deploy-portal-packages
```

### 3. Rebuild and start

```powershell
docker compose up -d --build
```

### 4. Image size

The Windows Server Core base image is ~5 GB (vs ~500 MB for Linux). The first build/pull will take significantly longer. This is normal for Windows containers.

### 5. PAC CLI location

The MSI installs PAC CLI to `%LOCALAPPDATA%\Microsoft\PowerAppsCLI` inside the container (i.e. `C:\Users\ContainerAdministrator\AppData\Local\Microsoft\PowerAppsCLI`). The SettingsService auto-detects it from PATH — no manual configuration needed.

### 6. GitHub Actions CI/CD

Docker build/push jobs now run on `windows-latest`. Build times are ~15-25 minutes (vs ~3-5 minutes on Linux). This is expected for Windows container builds.

---

## Installation

### Docker (Windows containers — GitHub Container Registry)
```powershell
# Switch Docker Desktop to Windows containers first!
docker pull ghcr.io/vglu/d365fo-deploy-portal:v1.8.0
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```powershell
docker pull vglu/d365fo-deploy-portal:v1.8.0
docker pull vglu/d365fo-deploy-portal:latest
```

### Docker Compose
```powershell
docker compose up -d --build
```

### Windows (self-contained, no Docker)
Download `DeployPortal-1.8.0-win-x64.zip` from Releases, extract and run `start.cmd`.

### Linux (convert-only, no deployment)
```bash
docker build -f Dockerfile.linux -t d365fo-deploy-portal-linux .
docker run -d -p 5000:5000 -v deploy-data:/app/data -v deploy-packages:/app/packages d365fo-deploy-portal-linux
```
