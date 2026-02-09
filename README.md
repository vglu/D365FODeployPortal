# D365FO Deploy Portal

A web-based tool for deploying Microsoft Dynamics 365 Finance & Operations packages to Power Platform environments. Provides a unified UI for uploading LCS/Unified packages, merging them, converting to Unified format, and deploying to one or multiple environments simultaneously — all with full deployment history and real-time logs.

## Features

- **Package Management** — Upload LCS, Unified, or other ZIP packages. Auto-detects package type by analyzing ZIP contents.
- **Package Merging** — Select 2+ LCS packages and merge them into a single package (combines metadata modules and components). Custom merge name with expandable source info.
- **LCS → Unified Conversion** — Automatically converts LCS packages to Unified format via `ModelUtil.exe` during deployment.
- **Multi-Environment Deploy** — Select a package and one or more target environments, then deploy to all at once.
- **Real-Time Logs** — Watch deployment output live via SignalR streaming.
- **Deployment History** — Full history of all deployments, filterable by package, environment, status, and date.
- **Import from Script** — Paste output of `Setup-ServicePrincipal.ps1` to bulk-create environments with parsed credentials.
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

## Quick Start (Development)

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [ModelUtil.exe](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-tools/models) — installed with D365FO development tools
- [PAC CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction) — `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`
- An Azure AD Service Principal for non-interactive PAC authentication (see [Setup](#service-principal-setup))

### Run

```powershell
# From project root
.\run.ps1

# Or manually
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

```bash
# Build and run with Docker Compose
docker compose up -d

# Open in browser
http://localhost:5000
```

### Docker Commands

```bash
# Build the image
docker build -t d365fo-deploy-portal .

# Run standalone (without Compose)
docker run -d \
  --name deploy-portal \
  -p 5000:5000 \
  -v deploy-data:/app/data \
  -v deploy-packages:/app/packages \
  d365fo-deploy-portal

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
| `DeployPortal__ConverterEngine` | `BuiltIn` | Converter engine (`BuiltIn` or `ModelUtil`) |
| `DeployPortal__ProcessingMode` | `Local` | Processing mode (`Local` or `Azure`) |

### Docker Limitations

- **ModelUtil.exe** is not available in the Linux container. Only the built-in converter is supported. This covers all standard LCS→Unified conversion scenarios.
- **No authentication** is built in. Do not expose the container port to the internet without a reverse proxy with authentication (e.g., nginx + OAuth2 Proxy, Traefik + basic auth).

## Publishing for Distribution

To create a self-contained application that can run on any Windows machine without .NET installed:

```powershell
.\publish.ps1
```

This creates a `publish/` folder containing:

```
publish/
├── DeployPortal.exe              # Self-contained application
├── start.cmd                     # Double-click to launch (opens browser automatically)
├── appsettings.json              # Default settings (empty paths — configure via UI)
├── Setup-ServicePrincipal.ps1    # Script for Azure AD setup
├── Setup-ServicePrincipal-Manual.md  # Manual instructions for admin
├── README.md                     # Quick start for target machine
└── ... (runtime files)
```

### Publish Options

```powershell
# Default (folder with all files)
.\publish.ps1

# Custom output directory
.\publish.ps1 -OutputDir "C:\Deploy\DeployPortal"

# Single-file executable (slower startup, but one file)
.\publish.ps1 -SingleFile
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
| Azure AD Service Principal | Only for deployment | For non-interactive PAC auth | Run `Setup-ServicePrincipal.ps1` or follow `Setup-ServicePrincipal-Manual.md` |

> **Note:** If you only upload already-Unified packages, `ModelUtil.exe` is not needed at all.

### Setup on Target Machine

1. **Copy** the `publish/` folder to the target machine
2. **Run** `start.cmd` (or `DeployPortal.exe --urls "http://localhost:5000"`)
3. **Open** `http://localhost:5000` in browser
4. **Go to Settings** → configure:
   - `ModelUtil.exe` path (if needed for LCS conversion)
   - `PAC CLI` path (if not in system PATH — auto-detected otherwise)
   - Package storage path (defaults to `Packages/` subdirectory)
5. **Go to Environments** → add environments:
   - **Option A:** Click "Import from Script" → paste output of `Setup-ServicePrincipal.ps1`
   - **Option B:** Manually enter Name, URL, Tenant ID, Application ID, Client Secret
6. **Upload packages** and start deploying

### Changing Port

```cmd
DeployPortal.exe --urls "http://localhost:8080"
```

### Remote Access

```cmd
DeployPortal.exe --urls "http://0.0.0.0:5000"
```

> **Warning:** No authentication is built in. Do not expose to the internet without a reverse proxy with auth.

## Service Principal Setup

A Service Principal (App Registration) in Azure AD is required for non-interactive PAC CLI authentication. One SP works for all environments in the same Azure AD tenant.

### Automated Setup

```powershell
# Full setup — creates SP and registers in all default environments
.\Setup-ServicePrincipal.ps1

# Full setup with custom environment list
.\Setup-ServicePrincipal.ps1 -Environments "env1.crm.dynamics.com","env2.crm.dynamics.com"

# Add a new environment to an existing SP
.\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com"
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

1. **UI Settings** (`usersettings.json` next to exe) — highest priority, managed via Settings page
2. **appsettings.json** — default values
3. **Built-in defaults** — auto-detection (PAC from PATH, storage in app directory)

### Settings Reference

| Setting | Description | Default |
|---------|-------------|---------|
| `ModelUtilPath` | Full path to `ModelUtil.exe` | Auto-detect from `%LocalAppData%\Microsoft\Dynamics365\` |
| `PacCliPath` | Full path to `pac.cmd` or `pac.exe` | Auto-detect from system PATH |
| `PackageStoragePath` | Directory for uploaded packages | `<app directory>/Packages` |
| `TempWorkingDir` | Temp directory for merge/convert | `%TEMP%/DeployPortal` |
| `DatabasePath` | Path to SQLite database file | `<app directory>/deploy-portal.db` |

### Data Storage

- **Database:** SQLite file (created automatically on first run)
- **Packages:** Stored as files in the configured storage directory
- **Secrets:** Client Secrets are encrypted using ASP.NET Core Data Protection API before saving to the database
- **Logs:** Rolling log files in `logs/` directory

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
├── run.ps1                            # Dev run script (hot reload)
├── publish.ps1                        # Build for distribution
├── Setup-ServicePrincipal.ps1         # Azure AD automation
├── Setup-ServicePrincipal-Manual.md   # Admin instructions
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

Internal tool. Not for public distribution.
