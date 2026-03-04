# D365FO Deploy Portal — Release Notes v1.6.0

**Release Date:** March 2026  
**Type:** Feature — LCS merge model conflict resolution

---

## New Features & Improvements

### LCS merge: resolve model name conflicts

When merging several LCS packages, the same model (e.g. `contosoapp`) may appear in different packages with different versions. Previously both variants were kept side by side. Now:

- **Conflict detection** — Before merge, the portal detects models that appear in more than one package (same module name, different versions).
- **Choose what to keep** — In the Merge dialog you can:
  - **Keep both (all variants)** — Current behavior: all variants remain in the merged package.
  - **Keep from package 1 / 2 / …** — Keep only the variant from the selected package; the other(s) are excluded from the merged result.
- **Apply to all** — One action can be applied to all conflicting models (e.g. “Keep from package 2” for every conflict), so you can resolve many conflicts in one click.

Example: Package 1 has `dynamicsax-contosoapp.2026.1.9.3.nupkg`, Package 2 has `dynamicsax-contosoapp.2026.3.3.4.nupkg`. The dialog shows the conflict and lets you keep the first, the second, or both.

- **API** — `POST /api/packages/merge/preview` returns `Strategy` and `Conflicts`. `POST /api/packages/merge` accepts optional `ModelConflictResolutions` to apply the same choices when merging via API.

---

## Technical Details

### Modified / New Files

- `PackageAnalyzer.cs` — `ExtractVersionFromModelFileName`; used for conflict display
- `LcsModelConflict.cs` — (new) DTOs for conflict and resolution
- `MergeEngine.cs` — `DetectLcsModelConflicts`, `MergeLcs(..., resolutions)`, selective copy and removal of model files
- `FileHelper.cs` — `CopyDirectoryRecursive` overload with file filter
- `MergeService.cs` — `GetMergeConflicts`, `MergePackagesAsync(..., modelResolutions)`
- `MergeRequestDto.cs` — `ModelConflictResolutions`, preview/conflict DTOs
- `Program.cs` — `/packages/merge/preview`, merge with resolutions
- `Packages.razor` — Merge dialog: conflict list, “Apply same action to all” dropdown

**Project:** `src/DeployPortal/DeployPortal.csproj` — version 1.6.0

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.6.0
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.6.0
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Download `DeployPortal-1.6.0-win-x64.zip` from the [Releases](https://github.com/vglu/D365FODeployPortal/releases) page.
2. Extract and run `start.cmd` or `DeployPortal.exe`.

---

## What's Next?

- [v1.5.4](RELEASE_NOTES_v1.5.4.md) — PAC auth isolation, Friendly Name options
- [v1.5.0 Release Notes](RELEASE_NOTES_v1.5.0.md) — SOLID refactoring, tests, E2E
- [v1.4.0 Release Notes](RELEASE_NOTES_v1.4.0.md) — Deployment History Archive

---

## Support

For questions and issues, please open an [Issue](https://github.com/vglu/D365FODeployPortal/issues) in the repository.  
Contact: [vhlu@sims-service.com](mailto:vhlu@sims-service.com) — [Sims Tech](https://sims-service.com/).
