# D365FO Deploy Portal — Release Notes v1.8.4

**Release Date:** September 2026  
**Type:** Bug fix — safe parallel PAC auth/bind + FO host probe + always-on env bind check

---

## Bug Fixes

### Wrong-environment deploy under parallel starts (#71 class)

When two deployments started close together with the same Service Principal, PAC could authenticate to the selected environment correctly (`auth who` OK) but later `package deploy` could target a different organization.

**Fix:**
- Stagger deployment starts by **90 seconds** with race-free slot reservation (parallel long installs still allowed via `MaxConcurrentDeployments`)
- Process-wide **exclusive auth/bind window** covering `auth create`, `auth select`, `auth who`, and pre-deploy probes
- Explicit **`pac auth select`** after create and again immediately before deploy
- **Always-on** pre-deploy bind check from `auth who` (URL host and/or organization name vs selected environment). The Friendly Name Settings toggle no longer disables this base check

Verified with Simulate mode: parallel queue to Cst-hfx-tst-03 and Cst-hfx-tst-05 — correct org bind, non-overlapping auth windows, no `package deploy`.

### Pre-deploy FO Application Host probe

Optional (default on) `pac package show` probe before a long deploy to fail early on ExternalOrchestration / host-not-ready states. Can be disabled in Settings.

### Shared Unified zip builder

One zip shared by FO host probe and package deploy so DLL + PackageAssets stay together.

### Favicon

Updated app favicon (SVG + PNG) linked from `App.razor`.

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```powershell
docker pull ghcr.io/vglu/d365fo-deploy-portal:v1.8.4
```

### Docker Hub
```powershell
docker pull vglu/d365fo-deploy-portal:v1.8.4
```

### Local publish
```powershell
.\scripts\publish.ps1
.\publish\start.cmd
```

Open http://localhost:5000

---

## Notes

- Parallelism remains a first-class requirement; only the short PAC auth/bind window is serialized.
- Turn off **Simulate deployment** in Settings for real installs.
