# D365FO Deploy Portal — Release Notes v1.6.1

**Release Date:** March 2026  
**Type:** Fix — Docker build in GitHub Actions

---

## Fixes

### Docker build (GitHub Actions)

- **Release workflow** — Fixed Docker image build failure: "Cache export is not supported for the docker driver." Added `docker/setup-buildx-action` so the build uses a builder that supports GitHub Actions cache (`cache-from` / `cache-to` type=gha). Releases and Docker Hub push now complete successfully.

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.6.1
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.6.1
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Download `DeployPortal-1.6.1-win-x64.zip` from the [Releases](https://github.com/vglu/D365FODeployPortal/releases) page.
2. Extract and run `start.cmd` or `DeployPortal.exe`.

---

## What's Next?

- [v1.6.0](RELEASE_NOTES_v1.6.0.md) — LCS merge model conflict resolution
- [v1.5.4](RELEASE_NOTES_v1.5.4.md) — PAC auth isolation, Friendly Name options

---

## Support

For questions and issues, please open an [Issue](https://github.com/vglu/D365FODeployPortal/issues) in the repository.  
Contact: [vhlu@sims-service.com](mailto:vhlu@sims-service.com) — [Sims Tech](https://sims-service.com/).
