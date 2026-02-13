# ============================================================
# D365FO Deploy Portal — Linux Docker Image (multi-stage build)
# ============================================================
# Build:   docker build -t d365fo-deploy-portal .
# Run:     docker run -p 5000:5000 -v deploy-data:/app/data -v deploy-packages:/app/packages d365fo-deploy-portal
# Compose: docker compose up -d
#
# CLI conversion (no web server):
#   docker run --rm -v /path/to/packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip /data/MyUnified.zip
#   docker run --rm -v C:\Downloads:/data vglu/d365fo-deploy-portal:latest convert /data/package.zip
#     (creates /data/package_Unified.zip)
# ============================================================

# ── Stage 1: Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first (better layer caching)
COPY Project4.sln ./
COPY src/DeployPortal/DeployPortal.csproj src/DeployPortal/
COPY src/DeployPortal.PackageOps/DeployPortal.PackageOps.csproj src/DeployPortal.PackageOps/
COPY src/DeployPortal.Functions/DeployPortal.Functions.csproj src/DeployPortal.Functions/
COPY src/DeployPortal.Tests/DeployPortal.Tests.csproj src/DeployPortal.Tests/

# Restore dependencies (cached unless .csproj files change)
RUN dotnet restore src/DeployPortal/DeployPortal.csproj
RUN dotnet restore src/DeployPortal.Tests/DeployPortal.Tests.csproj

# Copy everything else
COPY . .

# Run LCS→Unified conversion test (nested root) to verify converter works in Linux container
RUN dotnet build src/DeployPortal.Tests/DeployPortal.Tests.csproj -c Release --no-restore && \
    dotnet test src/DeployPortal.Tests/DeployPortal.Tests.csproj -c Release \
      --filter "FullyQualifiedName~ConvertToUnified_NestedLcsRoot_FindsModules" --no-build -v minimal

# Publish main app
RUN dotnet publish src/DeployPortal/DeployPortal.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install system dependencies + PAC CLI + Azure CLI (az)
# PAC: required for deployment. Azure CLI: required for "Deploy via Release Pipeline" (Universal Package upload).
# Pinned to 1.49.4 — last version targeting .NET 9 (2.x requires .NET 10)
ARG PAC_CLI_VERSION=1.49.4
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl libicu-dev unzip ca-certificates && \
    curl -sL -o /tmp/pac.nupkg \
      "https://www.nuget.org/api/v2/package/Microsoft.PowerApps.CLI.Core.linux-x64/${PAC_CLI_VERSION}" && \
    mkdir -p /tmp/pac-extract && \
    unzip -q /tmp/pac.nupkg -d /tmp/pac-extract && \
    cp -r /tmp/pac-extract/tools/. /usr/local/bin/ && \
    chmod +x /usr/local/bin/pac && \
    rm -rf /tmp/pac.nupkg /tmp/pac-extract && \
    apt-get purge -y unzip && apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/*

# Azure CLI — for "Deploy via Release Pipeline" (az artifacts universal publish)
RUN apt-get update && \
    curl -sL https://aka.ms/InstallAzureCLIDeb | bash && \
    rm -rf /var/lib/apt/lists/*

# Create directories for persistent data
RUN mkdir -p /app/data /app/packages /app/logs /app/keys

# Copy published application
COPY --from=build /app/publish .

# Environment variables — Docker-friendly defaults
# These can be overridden in docker-compose.yml or docker run -e
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DeployPortal__DatabasePath=/app/data/deploy-portal.db
ENV DeployPortal__PackageStoragePath=/app/packages
ENV DeployPortal__TempWorkingDir=/tmp/DeployPortal
ENV DeployPortal__DataProtectionKeysPath=/app/data/keys
ENV DeployPortal__UserSettingsPath=/app/data/usersettings.json
ENV DeployPortal__ConverterEngine=BuiltIn
# Full LCS template for Unified→LCS (ImportISVLicense.zip from CustomDeployablePackage). Override with DeployPortal__LcsTemplatePath if needed.
ENV DeployPortal__LcsTemplatePath=/app/Resources/LcsTemplate/ImportISVLicense.zip

# Volumes for persistent data
VOLUME ["/app/data", "/app/packages"]

EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/ || exit 1

ENTRYPOINT ["dotnet", "DeployPortal.dll"]
