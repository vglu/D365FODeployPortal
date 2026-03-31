# ============================================================
# D365FO Deploy Portal — Windows Docker Image (multi-stage build)
# ============================================================
# Requires: Docker Desktop switched to Windows containers
# Build:   docker build -t d365fo-deploy-portal .
# Run:     docker run -p 5000:5000 -v deploy-data:C:\app\data -v deploy-packages:C:\app\packages d365fo-deploy-portal
# Compose: docker compose up -d
#
# Why Windows? PAC CLI 'pac package deploy' is only available in the
# Windows MSI distribution. The cross-platform (.NET Core) builds of
# PAC CLI do NOT include this command. See Dockerfile.linux for a
# Linux image that supports conversion only (no deployment).
#
# CLI conversion (no web server):
#   docker run --rm -v C:\Downloads:C:\data d365fo-deploy-portal convert C:\data\MyLcs.zip C:\data\MyUnified.zip
# ============================================================

# ── Stage 1: Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0-windowsservercore-ltsc2022 AS build
WORKDIR C:\\src

# Copy solution and project files first (better layer caching)
COPY Project4.sln .
COPY src\DeployPortal\DeployPortal.csproj src\DeployPortal\
COPY src\DeployPortal.PackageOps\DeployPortal.PackageOps.csproj src\DeployPortal.PackageOps\
COPY src\DeployPortal.Functions\DeployPortal.Functions.csproj src\DeployPortal.Functions\
COPY src\DeployPortal.Tests\DeployPortal.Tests.csproj src\DeployPortal.Tests\

# Restore dependencies (cached unless .csproj files change)
RUN dotnet restore src\DeployPortal\DeployPortal.csproj
RUN dotnet restore src\DeployPortal.Tests\DeployPortal.Tests.csproj

# Copy everything else
COPY . .

# Run LCS->Unified conversion test to verify converter works in Windows container
RUN dotnet build src\DeployPortal.Tests\DeployPortal.Tests.csproj -c Release --no-restore && \
    dotnet test src\DeployPortal.Tests\DeployPortal.Tests.csproj -c Release \
      --filter "FullyQualifiedName~ConvertToUnified_NestedLcsRoot_FindsModules" --no-build -v minimal

# Publish main app
RUN dotnet publish src\DeployPortal\DeployPortal.csproj -c Release -o C:\app\publish --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0-windowsservercore-ltsc2022 AS runtime
WORKDIR C:\\app

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

# Install PAC CLI via MSI — the ONLY distribution that includes 'pac package deploy'.
# The cross-platform NuGet packages (Microsoft.PowerApps.CLI.Core.*) do NOT have this command.
RUN Invoke-WebRequest -Uri 'https://aka.ms/PowerAppsCLI' -OutFile C:\pac-cli.msi ; \
    Start-Process msiexec.exe -Wait -ArgumentList '/i', 'C:\pac-cli.msi', '/quiet', '/norestart' ; \
    Remove-Item C:\pac-cli.msi -Force ; \
    $pacDir = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerAppsCLI' ; \
    [Environment]::SetEnvironmentVariable('PATH', \
        [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + $pacDir, 'Machine')

# Install Azure CLI — required for 'Deploy via Release Pipeline' (az artifacts universal publish)
RUN Invoke-WebRequest -Uri 'https://aka.ms/installazurecliwindowsx64' -OutFile C:\azure-cli.msi ; \
    Start-Process msiexec.exe -Wait -ArgumentList '/i', 'C:\azure-cli.msi', '/quiet', '/norestart' ; \
    Remove-Item C:\azure-cli.msi -Force

# Create directories for persistent data
RUN New-Item -ItemType Directory -Force -Path C:\app\data, C:\app\packages, C:\app\logs, C:\app\keys | Out-Null

# Copy published application
COPY --from=build C:\\app\\publish .

# Verify PAC CLI installed correctly and 'package deploy' is available.
# This will fail the build if the MSI didn't install properly or deploy is missing.
RUN pac package deploy --help

# Environment variables — Docker-friendly defaults
# These can be overridden in docker-compose.yml or docker run -e
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DeployPortal__DatabasePath=C:\\app\\data\\deploy-portal.db
ENV DeployPortal__PackageStoragePath=C:\\app\\packages
ENV DeployPortal__TempWorkingDir=C:\\temp\\DeployPortal
ENV DeployPortal__DataProtectionKeysPath=C:\\app\\data\\keys
ENV DeployPortal__UserSettingsPath=C:\\app\\data\\usersettings.json
ENV DeployPortal__ConverterEngine=BuiltIn
# Full LCS template for Unified->LCS conversion. Override with DeployPortal__LcsTemplatePath if needed.
ENV DeployPortal__LcsTemplatePath=C:\\app\\Resources\\LcsTemplate\\ImportISVLicense.zip

# Volumes for persistent data
VOLUME ["C:\\app\\data", "C:\\app\\packages"]

EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["powershell", "-Command", \
         "try { $r = Invoke-WebRequest -Uri http://localhost:5000/ -UseBasicParsing -TimeoutSec 4; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"]

ENTRYPOINT ["dotnet", "DeployPortal.dll"]
