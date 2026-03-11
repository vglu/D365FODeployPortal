# D365FO Deploy Portal — Release Notes v1.7.0

**Release Date:** March 2026  
**Type:** Feature — Azure DevOps Integration & Release Pipeline Deployments

---

## New Features

### Azure DevOps Integration

- **Load from build** — Browse Azure DevOps pipelines directly from the Packages page, select a completed build, expand its artifact tree, pick a `.zip` artifact and load it into the portal with one click.
- **AzureDevOpsBuildService** — fetches pipelines, builds (with branch/status filter), artifact trees; downloads and uploads build artifacts automatically.

### Release Pipeline Deployments

- **Deploy via Release Pipeline** — New deployment mode on the Deploy page. Instead of direct PAC CLI deploy, the portal uploads the package as a Universal Package to an Azure DevOps feed and triggers an Azure DevOps Release Pipeline.
- **AzureDevOpsReleaseService** — creates Universal Package versions, triggers releases, polls deployment status and feeds progress back to the portal UI.
- **Upload scripts** — `scripts/upload-universal-package.ps1` (standalone) and `scripts/upload-universal-package-from-portal.ps1` (download from portal then upload to ADO feed).

### UI Improvements

- **Packages page** — Bulk delete selected packages; improved drag-and-drop upload UX; "Load from build" dialog with artifact tree browser (`ArtifactTreeNode` component).
- **Environments page** — Export / Import (backup & restore) all environments as JSON.
- **Deploy page** — Release Pipeline mode with feed name, pipeline selection and status tracking.
- **Settings page** — New section: Azure DevOps (organization, project, PAT) and Release Pipeline (feed name).

### Infrastructure

- **VerifyBuildArtifacts** — helper console project to validate downloaded build artifacts before packaging.
- **ExceptionHelper** — utility for friendly error message extraction.
- **docs/Release-Pipeline-Universal-Package.md** — full guide for setting up the Release Pipeline deployment flow.

---

## Bug Fixes

- Fixed stale pre-compressed static files (`MudBlazor.min.js.br` / `.gz`) causing Blazor circuit crash (`mudElementRef.addOnBlurEvent was undefined`) after publish update.

---

## Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.7.0
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.7.0
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (self-contained)
Download `DeployPortal-1.7.0-win-x64.zip` from Releases, extract and run `start.cmd`.

---

## Configuration

New settings for Azure DevOps integration (configure in Settings page or `usersettings.json`):

| Setting | Description |
|---|---|
| `AzureDevOpsOrganization` | ADO organization name (e.g. `contoso`) |
| `AzureDevOpsProject` | ADO project name |
| `AzureDevOpsPatEncrypted` | Personal Access Token (encrypted, set via UI) |
| `ReleasePipelineFeedName` | Universal Package feed name (default: `Packages`) |
