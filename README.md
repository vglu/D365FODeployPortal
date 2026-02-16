# D365FO Deploy Portal

A web-based tool for deploying Microsoft Dynamics 365 Finance & Operations packages to Power Platform environments. Provides a unified UI for uploading LCS/Unified packages, merging them, converting to Unified format, and deploying to one or multiple environments simultaneously — all with full deployment history and real-time logs.

## Features

- **Package Management** — Upload LCS, Unified, or other ZIP packages. Auto-detects package type by analyzing ZIP contents.
- **Package Merging** — Select 2+ LCS packages and merge them into a single package (combines metadata modules and components). Custom merge name with expandable source info.
- **LCS → Unified Conversion** — Automatically converts LCS packages to Unified format via `ModelUtil.exe` during deployment.
- **Multi-Environment Deploy** — Select a package and one or more target environments, then deploy to all at once.
- **Real-Time Logs** — Watch deployment output live via SignalR streaming.
- **Deployment History** — Full history of all deployments, filterable by package, environment, status, and date.
- **Import from Script** — Paste output of `scripts/Setup-ServicePrincipal.ps1` to bulk-create environments with parsed credentials.
- **Configurable Paths** — All tool paths (ModelUtil, PAC CLI, storage) configurable via Settings page. No hardcoded paths.
- **Self-Contained Distribution** — Publish as a single folder with embedded .NET Runtime. No prerequisites on the target machine (except external tools).

## Architecture

```
Blazor Server UI  →  ASP.NET Core Services  →  Background Workers  →  External Tools
     ↑                      ↓                                          (ModelUtil.exe, PAC CLI)
     └── SignalR ──── DeployLogHub
                            ↓
                     SQLite Database
```

| Component | Technology |
|-----------|-----------|
| Frontend | Blazor Server + MudBlazor 8.x (Material Design) |
| Backend | ASP.NET Core 9.0 |
| Database | SQLite via Entity Framework Core |
| Background Jobs | `BackgroundService` + `Channel<T>` |
| Real-Time Logs | SignalR |
| Secret Storage | ASP.NET Core Data Protection API (encrypted) |
| Logging | Serilog (console + rolling file) |

## Pages

| Page | URL | Description |
|------|-----|-------------|
| **Dashboard** | `/` | Package/environment/deployment counters, recent deployments, setup warnings |
| **Packages** | `/packages` | Upload packages, merge, view list with sorting/search/expandable merge info |
| **Environments** | `/environments` | CRUD for target environments, bulk import from script output |
| **Deploy** | `/deploy` | Select package + environments → start deployment |
| **History** | `/deployments` | Full deployment history with status, click for details |
| **Deployment Detail** | `/deployments/{id}` | Real-time log viewer for a specific deployment |
| **Settings** | `/settings` | Configure tool paths, storage, view tool status |

## Usage examples

Summary of all ways to run and use the application:

| Scenario | Command / Link |
|----------|-----------------|
| **Development (local)** | **PowerShell:** `.\scripts\run.ps1` or `dotnet watch --project src/DeployPortal --urls "http://localhost:5137"` — **CMD:** `powershell -NoProfile -File scripts\run.ps1` |
| **Docker — web UI** | `docker compose up -d` → open http://localhost:5000 |
| **Docker — CLI conversion** | See [Command-line conversion](#command-line-conversion-cli) below (paths for Windows CMD, PowerShell, Linux). |
| **Published app (Windows)** | **PowerShell:** `.\scripts\publish.ps1` — **CMD:** `powershell -NoProfile -File scripts\publish.ps1` — then run `publish\start.cmd` or `publish\DeployPortal.exe` |
| **CLI conversion (local, no UI)** | **PowerShell:** `dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip" "C:\Packages\MyUnified.zip"` — **CMD:** `dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip" "C:\Packages\MyUnified.zip"` |

**Releases & Packages:** ready-made builds — [GitHub Releases](https://github.com/vglu/D365FODeployPortal/releases) (Windows ZIP) and image [ghcr.io/vglu/d365fo-deploy-portal](https://github.com/vglu/D365FODeployPortal/pkgs/container/d365fo-deploy-portal). See [documents/RELEASES_AND_PACKAGES.md](documents/RELEASES_AND_PACKAGES.md).

See sections below for details.

## Quick Start (Development)

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [ModelUtil.exe](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-tools/models) — installed with D365FO development tools
- [PAC CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction) — `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`
- An Azure AD Service Principal for non-interactive PAC authentication (see [Setup](#service-principal-setup))

### Run

**PowerShell (from project root):**
```powershell
.\scripts\run.ps1
# Or manually:
dotnet watch --project src/DeployPortal --urls "http://localhost:5137"
```

**CMD (from project root):**
```cmd
powershell -NoProfile -File scripts\run.ps1
REM Or manually:
dotnet watch --project src/DeployPortal --urls "http://localhost:5137"
```

Open `http://localhost:5137` in your browser.

### First Steps

1. Go to **Settings** → configure paths to `ModelUtil.exe` and `PAC CLI` (auto-detected from PATH if available)
2. Go to **Environments** → add target Power Platform environments (or use **Import from Script**)
3. Go to **Packages** → upload LCS or Unified ZIP packages
4. Go to **Deploy** → select a package, check target environments, click **Start Deploy**

## Docker

The application can be distributed as a Linux Docker container. This is the easiest way to deploy on any machine with Docker installed — no .NET, no dependencies.

### Quick Start (Docker)

The image is published to **GitHub Container Registry**: `ghcr.io/vglu/d365fo-deploy-portal` (see [Releases & Packages](documents/RELEASES_AND_PACKAGES.md)). For local build:

```bash
# Build and run with Docker Compose
docker compose up -d

# Open in browser
http://localhost:5000
```

### Docker Commands

```bash
# Build the image (use your tag for push to registry)
docker build -t d365fo-deploy-portal .
docker build -t vglu/d365fo-deploy-portal:latest .

# Run standalone (without Compose)
docker run -d \
  --name deploy-portal \
  -p 5000:5000 \
  -v deploy-data:/app/data \
  -v deploy-packages:/app/packages \
  d365fo-deploy-portal

# Run with CLI conversion only (no web server, one-off)
docker run --rm -v /path/to/packages:/data d365fo-deploy-portal convert /data/MyLcs.zip /data/Unified.zip

# View logs
docker compose logs -f

# Stop
docker compose down

# Rebuild after code changes
docker compose up -d --build
```

### What's Inside the Container

| Component | Status |
|-----------|--------|
| .NET 9.0 Runtime | Included |
| PAC CLI | Installed automatically |
| Built-in LCS→Unified converter | Active (no ModelUtil.exe needed) |
| SQLite database | Created automatically in `/app/data/` |
| Uploaded packages | Stored in `/app/packages/` |

### Docker Volumes

| Volume | Container Path | Purpose |
|--------|---------------|---------|
| `deploy-portal-data` | `/app/data` | Database, encryption keys, user settings |
| `deploy-portal-packages` | `/app/packages` | Uploaded packages |

> **Important:** These volumes persist data across container restarts and rebuilds. Do not use `docker compose down -v` unless you want to erase all data.

### Changing the Port

```yaml
# In docker-compose.yml, change:
ports:
  - "8080:5000"    # host:container
```

Or override the internal port:

```yaml
environment:
  - ASPNETCORE_URLS=http://+:8080
ports:
  - "8080:8080"
```

### Environment Variables

All settings can be configured via environment variables in `docker-compose.yml`:

| Variable | Default | Description |
|----------|---------|-------------|
| `DeployPortal__DatabasePath` | `/app/data/deploy-portal.db` | SQLite database location |
| `DeployPortal__PackageStoragePath` | `/app/packages` | Package storage directory |
| `DeployPortal__TempWorkingDir` | `/tmp/DeployPortal` | Temporary directory for merge/convert |
| `DeployPortal__DataProtectionKeysPath` | `/app/data/keys` | Encryption keys directory |
| `DeployPortal__UserSettingsPath` | `/app/data/usersettings.json` | UI settings file (in volume so they persist) |
| `DeployPortal__ConverterEngine` | `BuiltIn` | Converter engine (`BuiltIn` or `ModelUtil`) |
| `DeployPortal__ProcessingMode` | `Local` | Processing mode (`Local` or `Azure`) |

### Command-line conversion (CLI)

You can use the container only for LCS → Unified conversion, without starting the web server. Pass `convert` as the first argument and mount a folder with your package.

**Examples:**

**Windows — CMD:** (use `C:\Packages` or your folder; inside container the mount is `/data`)
```cmd
docker run --rm -v C:\Packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip /data/MyUnified.zip
REM Output omitted — creates /data/MyLcs_Unified.zip
docker run --rm -v C:\Packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip
```

**Windows — PowerShell:**
```powershell
docker run --rm -v C:\Packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip /data/MyUnified.zip
# Output omitted — creates /data/MyLcs_Unified.zip
docker run --rm -v C:\Packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip
```

**Linux/macOS:**
```bash
docker run --rm -v /home/user/packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcsPackage.zip /data/Unified.zip
```

**Using environment variables (e.g. in scripts):**

**CMD:**
```cmd
docker run --rm -v C:\Packages:/data -e CONVERT_INPUT=/data/package.zip -e CONVERT_OUTPUT=/data/out.zip vglu/d365fo-deploy-portal:latest convert
```

**PowerShell:**
```powershell
docker run --rm -v C:\Packages:/data -e CONVERT_INPUT=/data/package.zip -e CONVERT_OUTPUT=/data/out.zip vglu/d365fo-deploy-portal:latest convert
```

**CLI conversion without Docker (local .NET):**

**PowerShell (from repository root):**
```powershell
dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip" "C:\Packages\MyUnified.zip"
# Default output: same folder, <name>_Unified.zip
dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip"
```

**CMD (from repository root):**
```cmd
dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip" "C:\Packages\MyUnified.zip"
REM Default output: same folder, <name>_Unified.zip
dotnet run --project src/DeployPortal -- convert "C:\Packages\MyLcs.zip"
```

If the output path is omitted, the result is written as `<input name>_Unified.zip` in the same directory as the input file. Exit codes: `0` = success, `1` = usage/input error, `2` = template not found, `3` = conversion error.

### Docker Limitations

- **ModelUtil.exe** is not available in the Linux container. Only the built-in converter is supported. This covers all standard LCS→Unified conversion scenarios.
- **No authentication** is built in. Do not expose the container port to the internet without a reverse proxy with authentication (e.g., nginx + OAuth2 Proxy, Traefik + basic auth).

## Publishing for Distribution

To create a self-contained application that can run on any Windows machine without .NET installed:

**PowerShell:**
```powershell
.\scripts\publish.ps1
```

**CMD:**
```cmd
powershell -NoProfile -File scripts\publish.ps1
```

This creates a `publish/` folder containing:

```
publish/
├── DeployPortal.exe              # Self-contained application
├── start.cmd                     # Double-click to launch (opens browser automatically)
├── appsettings.json              # Default settings (empty paths — configure via UI)
├── Setup-ServicePrincipal.ps1    # Azure AD setup (copied from repo scripts/)
├── check-prerequisites.ps1       # Pre-flight check (copied from repo scripts/)
├── Setup-ServicePrincipal-Manual.md  # Manual instructions for admin
├── README.md                     # Quick start for target machine
└── ... (runtime files)
```

### Publish Options

**PowerShell:**
```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -OutputDir "C:\Deploy\DeployPortal"
.\scripts\publish.ps1 -SingleFile
```

**CMD:** (paths work the same)
```cmd
powershell -NoProfile -File scripts\publish.ps1
powershell -NoProfile -File scripts\publish.ps1 -OutputDir "C:\Deploy\DeployPortal"
powershell -NoProfile -File scripts\publish.ps1 -SingleFile
```

## Transferring to Another Machine

### What to Copy

Copy the entire `publish/` folder to the target machine. That's it — .NET Runtime is embedded.

### What's Needed on the Target Machine

| Component | Required? | When | How to Install |
|-----------|-----------|------|----------------|
| .NET Runtime | **No** | — | Bundled in self-contained publish |
| ModelUtil.exe | Only for LCS→Unified conversion | When deploying LCS packages | Installed with [D365FO development tools](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-tools/models) |
| PAC CLI | Only for deployment | When deploying to Power Platform | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` |
| Azure AD Service Principal | Only for deployment | For non-interactive PAC auth | Run `scripts\Setup-ServicePrincipal.ps1` or follow `documents/Setup-ServicePrincipal-Manual.md` |

> **Note:** If you only upload already-Unified packages, `ModelUtil.exe` is not needed at all.

### Setup on Target Machine

1. **Copy** the `publish/` folder to the target machine (e.g. `C:\DeployPortal`).
2. **Run** from the publish folder:
   - **CMD:** `start.cmd` or `DeployPortal.exe --urls "http://localhost:5000"`
   - **PowerShell:** `.\start.cmd` or `.\DeployPortal.exe --urls "http://localhost:5000"`
3. **Open** `http://localhost:5000` in browser
4. **Go to Settings** → configure:
   - `ModelUtil.exe` path (if needed for LCS conversion)
   - `PAC CLI` path (if not in system PATH — auto-detected otherwise)
   - Package storage path (defaults to `Packages/` subdirectory)
5. **Go to Environments** → add environments:
   - **Option A:** Click "Import from Script" → paste output of `scripts\Setup-ServicePrincipal.ps1`
   - **Option B:** Manually enter Name, URL, Tenant ID, Application ID, Client Secret
6. **Upload packages** and start deploying

### Changing Port

**CMD:** `DeployPortal.exe --urls "http://localhost:8080"`  
**PowerShell:** `.\DeployPortal.exe --urls "http://localhost:8080"`

### Remote Access

**CMD:** `DeployPortal.exe --urls "http://0.0.0.0:5000"`  
**PowerShell:** `.\DeployPortal.exe --urls "http://0.0.0.0:5000"`

> **Warning:** No authentication is built in. Do not expose to the internet without a reverse proxy with auth.

## Service Principal Setup

A Service Principal (App Registration) in Azure AD is required for non-interactive PAC CLI authentication. One SP works for all environments in the same Azure AD tenant.

### Automated Setup

**PowerShell (from folder containing the script):**
```powershell
.\scripts\Setup-ServicePrincipal.ps1
.\scripts\Setup-ServicePrincipal.ps1 -Environments "env1.crm.dynamics.com","env2.crm.dynamics.com"
.\scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com"
```

**CMD (same folder):**
```cmd
powershell -NoProfile -File scripts\Setup-ServicePrincipal.ps1
powershell -NoProfile -File scripts\Setup-ServicePrincipal.ps1 -Environments "env1.crm.dynamics.com","env2.crm.dynamics.com"
powershell -NoProfile -File scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com"
```

**Required permissions to run:** Global Administrator or Application Administrator in Azure AD, System Administrator in Power Platform environments.

### Manual Setup

See `Setup-ServicePrincipal-Manual.md` for step-by-step instructions to give to your Azure AD administrator.

### Output Format

The script outputs credentials in this format (also saved to a `.txt` file):

```
Application (Client) ID:  xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Directory (Tenant) ID:    xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Client Secret:            xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Environments (OK):        env1.crm.dynamics.com, env2.crm.dynamics.com
```

This output can be pasted directly into the **Import from Script** dialog on the Environments page.

## Configuration

### Settings Priority

1. **UI Settings** (`usersettings.json`) — highest priority, managed via Settings page. Path (Windows): **CMD** `%LocalAppData%\DeployPortal\usersettings.json` — **PowerShell** `$env:LOCALAPPDATA\DeployPortal\usersettings.json`. In Docker: `/app/data/usersettings.json` (in volume).
2. **appsettings.json** — default values
3. **Built-in defaults** — auto-detection (PAC from PATH, storage in app directory)

### Settings Reference

| Setting | Description | Default |
|---------|-------------|---------|
| `ModelUtilPath` | Full path to `ModelUtil.exe` | Auto-detect: **CMD** `%LocalAppData%\Microsoft\Dynamics365\` — **PS** `$env:LOCALAPPDATA\Microsoft\Dynamics365\` |
| `PacCliPath` | Full path to `pac.cmd` or `pac.exe` | Auto-detect from system PATH |
| `PackageStoragePath` | Directory for uploaded packages | `<app directory>\Packages` (e.g. `C:\DeployPortal\Packages`) |
| `TempWorkingDir` | Temp directory for merge/convert | **CMD** `%TEMP%\DeployPortal` — **PS** `$env:TEMP\DeployPortal` |
| `DatabasePath` | Path to SQLite database file | `<app directory>\deploy-portal.db` |
| `UserSettingsPath` | Path to UI settings JSON (optional) | **CMD** `%LocalAppData%\DeployPortal\usersettings.json` — **PS** `$env:LOCALAPPDATA\DeployPortal\usersettings.json` |
| `LcsTemplatePath` | Optional path to LCS template (folder or .zip) for **Unified→LCS** conversion | *(empty)* |

### LCS template (Unified→LCS)

When you convert a package from Unified back to LCS, the default output contains only the converted modules, `HotfixInstallationInfo.xml`, and license files — without the full LCS “skeleton” (e.g. `AXUpdateInstaller.exe`, DLLs, Scripts, other files). If you need the result to match the structure of an original LCS package (e.g. for deployment or comparison), set **LCS Template** in Settings:

- **Path:** A folder or a .zip file that contains the full LCS structure (one root folder with `AOSService`, `HotfixInstallationInfo.xml`, executables, Scripts, etc.). Good options: an LCS package from the asset library, or a **Custom deployable package** from your dev machine, e.g. `PackagesLocalDirectory\bin\CustomDeployablePackage\ImportISVLicense.zip`. Full path examples: **CMD** `C:\Users\%USERNAME%\AppData\Local\Microsoft\Dynamics365\RuntimeSymLinks\<env>\PackagesLocalDirectory\bin\CustomDeployablePackage\ImportISVLicense.zip` — **PowerShell** `$env:LOCALAPPDATA\Microsoft\Dynamics365\RuntimeSymLinks\<env>\PackagesLocalDirectory\bin\CustomDeployablePackage\ImportISVLicense.zip`. You can leave `Scripts/License` in the template empty; the converter overwrites it.
- **Behaviour:** The converter copies the template into the output directory, then overwrites `HotfixInstallationInfo.xml`, replaces the contents of `AOSService/Packages/files` with the converted modules (`.zip`), and restores license files under `AOSService/Scripts/License`. All other files (exe, DLLs, Scripts .ps1/.psm1, etc.) come from the template unchanged.

Leave the setting empty to keep the current “minimal” LCS output.

**In Docker:** the image includes a full LCS template `ImportISVLicense.zip` (from CustomDeployablePackage) at `/app/Resources/LcsTemplate/ImportISVLicense.zip`, used by default for Unified→LCS. To use a different template, set `DeployPortal__LcsTemplatePath` or mount your own (e.g. `/app/lcs-template.zip`).

**About .nupkg / module files:** The converter generates from the Unified package both `dynamicsax-*.zip` in `AOSService/Packages/files/` and `dynamicsax-*.nupkg` in `AOSService/Packages/`. Each `.nupkg` is a NuGet-style zip (`.nuspec` manifest plus module content). You do not put module files in the template; the template only provides the folder structure (and optionally the Microsoft runtime files — exe, DLLs — if you use your own LCS package as template).

**Round-trip quality:** With the **built-in template** (LcsSkeleton), the round-trip LCS is intentionally minimal: `HotfixInstallationInfo.xml`, modules as `dynamicsax-*.zip` in `Packages/files`, and `Scripts/License`. There is no full Scripts content (exe, DLLs, install scripts) and no `.nupkg` in Packages — the converter does not generate those. If you need a round-trip LCS that matches the original (same Scripts, layout, etc.), set **LCS Template** to a real LCS package (unpacked folder or .zip) that you have from the LCS asset library or your own export.

**Creating a template from your own package:** To use a full LCS package (e.g. a main/production package from the asset library) as the template, first remove license files so the template does not carry production licenses. Use the script `scripts/New-LcsTemplateFromPackage.ps1`: it extracts the package, deletes `AOSService/Scripts/License/*`, and optionally removes `Packages/files/*.zip` and `Packages/*.nupkg` so the template is a skeleton only. Example paths: **PowerShell** `.\scripts\New-LcsTemplateFromPackage.ps1 -SourceZip "C:\Packages\MyProduction.zip" -RemovePackagePayload` — **CMD** `powershell -NoProfile -File scripts\New-LcsTemplateFromPackage.ps1 -SourceZip "C:\Packages\MyProduction.zip" -RemovePackagePayload`. Then set **LcsTemplatePath** in Settings to the resulting `*_NoLicenses.zip` (or copy it to `Resources/LcsTemplate/` and point to it).

### Data Storage

- **Database:** SQLite file (created automatically on first run)
- **Packages:** Stored as files in the configured storage directory
- **Secrets:** Client Secrets are encrypted using ASP.NET Core Data Protection API before saving to the database
- **Logs:** Rolling log files in `logs/` directory

## REST API

The portal exposes a REST API for package management: list, upload, convert (LCS↔Unified), merge, refresh licenses, and download. Use it from scripts, Postman, or Azure DevOps pipelines. Interactive documentation is available via **Swagger UI** when the app is running:

- **Swagger UI:** `http://localhost:5000/swagger` (or your base URL + `/swagger`)

**Base URL:** In the examples below, `BASE_URL` is `http://localhost:5000` (Docker) or `http://localhost:5137` (local `.\scripts\run.ps1`). Replace as needed.

### Endpoints and curl examples

| Action | Method | Endpoint | Description |
|--------|--------|----------|-------------|
| List packages | `GET` | `/api/packages` | Returns all packages (id, name, type, size, etc.) |
| Get one package | `GET` | `/api/packages/{id}` | Returns one package by ID |
| Upload package | `POST` | `/api/packages/upload` | Upload a .zip (multipart/form-data); optional: `packageType`, `devOpsTaskUrl` |
| Convert to Unified | `POST` | `/api/packages/{id}/convert/unified` | Converts LCS/Merged package to Unified |
| Convert to LCS | `POST` | `/api/packages/{id}/convert/lcs` | Converts Unified package to LCS |
| Merge packages | `POST` | `/api/packages/merge` | Merges 2+ packages; body: `{"packageIds":[1,2],"mergeName":"MyMerge"}` |
| Refresh licenses | `POST` | `/api/packages/{id}/refresh-licenses` | Re-scans package for license files |
| Download package | `GET` | `/api/packages/{id}/download` | Returns the package file (attachment) |
| List licenses | `GET` | `/api/packages/{id}/licenses` | Returns list of license file paths in the package |

**Examples (curl):**

```bash
# Set base URL (Docker default)
BASE_URL="http://localhost:5000"

# List all packages
curl -s "$BASE_URL/api/packages"

# Get one package (e.g. id=1)
curl -s "$BASE_URL/api/packages/1"

# Upload a package (required: form field "file" with .zip)
curl -X POST "$BASE_URL/api/packages/upload" \
  -F "file=@/path/to/MyLcs.zip"
# Optional form fields: packageType=LCS, devOpsTaskUrl=https://...

# Convert package 1 to Unified
curl -X POST "$BASE_URL/api/packages/1/convert/unified"

# Convert package 2 to LCS
curl -X POST "$BASE_URL/api/packages/2/convert/lcs"

# Merge packages 1 and 2 (optional mergeName)
curl -X POST "$BASE_URL/api/packages/merge" \
  -H "Content-Type: application/json" \
  -d '{"packageIds":[1,2],"mergeName":"MergedRelease"}'

# Refresh license info for package 1
curl -X POST "$BASE_URL/api/packages/1/refresh-licenses"

# Download package 1 (saves to file)
curl -o MyPackage.zip "$BASE_URL/api/packages/1/download"

# List license files in package 1
curl -s "$BASE_URL/api/packages/1/licenses"
```

### Azure DevOps Pipeline example

You can call the API from a pipeline to upload a package, convert it, or download the result. Example (Bash task):

```yaml
# Upload build artifact, convert to Unified, then download (example)
- task: Bash@3
  displayName: 'Upload and convert package via Deploy Portal API'
  inputs:
    targetType: inline
    script: |
      BASE_URL="$(DEPLOY_PORTAL_URL)"   # e.g. https://deploy-portal.mycompany.com
      ZIP_PATH="$(Build.ArtifactStagingDirectory)/MyPackage.zip"
      curl -s -X POST "$BASE_URL/api/packages/upload" -F "file=@$ZIP_PATH" -o upload.json
      ID=$(jq -r '.id' upload.json)
      curl -s -X POST "$BASE_URL/api/packages/$ID/convert/unified" -o convert.json
      UNIFIED_ID=$(jq -r '.id' convert.json)
      curl -o "$(Build.ArtifactStagingDirectory)/Unified.zip" "$BASE_URL/api/packages/$UNIFIED_ID/download"
```

For **merge** in a pipeline, POST to `/api/packages/merge` with a JSON body containing `packageIds` and optional `mergeName`, then use the returned package `id` for deploy or download.

### Deploy via Release Pipeline (Universal Package)

To upload a package to Azure Artifacts (Universal Package) and start an Azure DevOps Release from the portal or from a script, see **[documents/Release-Pipeline-Universal-Package.md](documents/Release-Pipeline-Universal-Package.md)** — setup of the Release artifact (Azure Artifacts / Universal), feed name, script usage, and troubleshooting.

## Package Types

| Type | Description | Auto-Detection |
|------|-------------|----------------|
| **LCS** | Original Dynamics 365 package format | Contains `AOSService/` or `HotfixInstallationInfo.xml` |
| **Unified** | Converted format for Power Platform | Contains `TemplatePackage.dll` |
| **Merged** | Result of merging 2+ LCS packages | Created by the app, shows source packages in expandable row |
| **Other** | Any other ZIP file | None of the above patterns matched |

### Deployment Flow

```
LCS Package  ──→  [Merge (optional)]  ──→  [Convert to Unified]  ──→  Deploy via PAC CLI
Unified Package  ──────────────────────────────────────────────────→  Deploy via PAC CLI
```

If you upload an already-Unified package, the conversion step is skipped.

## Development

### Project Structure

```
d:\Projects\Project4\
├── README.md                          # This file
├── scripts/
│   ├── run.ps1                        # Dev run script (hot reload)
│   ├── publish.ps1                    # Build for distribution
│   ├── Setup-ServicePrincipal.ps1     # Azure AD automation
│   └── ...                            # check-prerequisites.ps1, run-tests.ps1, etc.
├── documents/
│   └── Setup-ServicePrincipal-Manual.md   # Admin instructions
├── Project4.sln                       # Solution file
└── src/
    ├── DeployPortal/                  # Main Blazor Server application
    │   ├── Program.cs                 # DI, middleware, database setup
    │   ├── appsettings.json           # Default configuration
    │   ├── Data/
    │   │   └── AppDbContext.cs         # EF Core DbContext
    │   ├── Models/
    │   │   ├── Package.cs              # Package entity
    │   │   ├── Environment.cs          # Environment entity
    │   │   ├── Deployment.cs           # Deployment entity
    │   │   └── DeploymentStatus.cs     # Status enum
    │   ├── Services/
    │   │   ├── SettingsService.cs       # Runtime settings management
    │   │   ├── PackageService.cs        # Upload, type detection, storage
    │   │   ├── MergeService.cs          # LCS package merging
    │   │   ├── ConvertService.cs        # LCS → Unified conversion
    │   │   ├── DeployService.cs         # PAC CLI authentication & deploy
    │   │   ├── DeploymentOrchestrator.cs # Background pipeline
    │   │   ├── EnvironmentService.cs    # Environment CRUD
    │   │   ├── SecretProtectionService.cs # Secret encryption
    │   │   └── CredentialParser.cs      # Parse script output
    │   ├── Hubs/
    │   │   └── DeployLogHub.cs          # SignalR for live logs
    │   └── Components/
    │       ├── Layout/
    │       │   ├── MainLayout.razor     # App shell
    │       │   └── NavMenu.razor        # Side navigation
    │       └── Pages/
    │           ├── Home.razor           # Dashboard
    │           ├── Packages.razor       # Package management
    │           ├── Environments.razor   # Environment management
    │           ├── Deploy.razor         # Deployment launcher
    │           ├── Deployments.razor    # History list
    │           ├── DeploymentDetail.razor # Live log viewer
    │           └── Settings.razor       # Configuration
    └── DeployPortal.Tests/            # E2E tests (Playwright)
        └── E2ETests.cs
```

### Running Tests

```powershell
cd src/DeployPortal.Tests
dotnet test --logger "console;verbosity=detailed"
```

Tests use Playwright for browser automation. They start a separate instance of the app on port 5199 with a temporary database.

## License

This project is developed by **[Sims Tech](https://sims-service.com/)**.

It is free and open source: you may **use, copy, modify, and distribute** it freely. The software is provided **without warranty of any kind**; all risks of use are solely with the user. See the [LICENSE](LICENSE) file (MIT License) for the full legal terms.
