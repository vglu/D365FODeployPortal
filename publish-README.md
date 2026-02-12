# DeployPortal — D365FO Package Deployment Tool

## Quick Start

1. Run start.cmd — opens browser automatically
2. Go to **Settings** — configure paths to ModelUtil.exe and PAC CLI
3. Go to **Environments** — add target Power Platform environments
4. Go to **Packages** — upload packages (LCS, Unified, or Other)
5. Go to **Deploy** — select package + environments and deploy

## Prerequisites on Target Machine

| Component       | Required? | How to install                                           |
|-----------------|-----------|----------------------------------------------------------|
| .NET Runtime    | No        | Bundled (self-contained publish)                         |
| ModelUtil.exe   | For LCS conversion | Installed with D365FO dev tools              |
| PAC CLI         | For deployment | dotnet tool install --global Microsoft.PowerApps.CLI.Tool |
| Azure SP        | For deployment | Run Setup-ServicePrincipal.ps1 or follow Manual.md |

## Configuration

- All settings are configurable from the **Settings** page in the UI
- Settings are saved to usersettings.json in the app directory
- Database (deploy-portal.db) is created automatically in the app directory
- Package storage defaults to Packages/ subdirectory

## Ports

Default: http://localhost:5000
To change: DeployPortal.exe --urls "http://localhost:8080"
To allow remote access: DeployPortal.exe --urls "http://0.0.0.0:5000"
