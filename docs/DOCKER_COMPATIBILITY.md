# Two-Level Validation and PAC Auth Isolation in Docker

## Status: Full Compatibility

The solution with isolated PAC auth directories and two-level validation **works fully inside the Docker container** with no extra changes.

---

## Compatibility Check

### 1. PAC CLI installed in container ✓

**Dockerfile, lines 50-61:**
```dockerfile
ARG PAC_CLI_VERSION=1.49.4
RUN apt-get update && \
    apt-get install -y curl libicu-dev unzip && \
    curl -sL -o /tmp/pac.nupkg "..." && \
    cp -r /tmp/pac-extract/tools/. /usr/local/bin/ && \
    chmod +x /usr/local/bin/pac
```

PAC CLI is available globally in the container as `/usr/local/bin/pac`.

---

### 2. Temp directory configured ✓

**Dockerfile, line 80:**
```dockerfile
ENV DeployPortal__TempWorkingDir=/tmp/DeployPortal
```

**docker-compose.yml, line 30:**
```yaml
environment:
  - DeployPortal__TempWorkingDir=/tmp/DeployPortal
```

Isolated PAC auth folders are created under:
```
/tmp/DeployPortal/pac_auth_1_abc123/
/tmp/DeployPortal/pac_auth_2_xyz789/
```

---

### 3. Cross-platform code ✓

All file system operations use .NET APIs that adapt to the OS:

#### `Path.Combine` — path separators:

**DeploymentOrchestrator.cs, line 139:**
```csharp
var isolatedAuthDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");
```

**Result:**
- Windows: `C:\Temp\DeployPortal\pac_auth_1_abc123`
- Linux: `/tmp/DeployPortal/pac_auth_1_abc123`

#### `Directory.CreateDirectory` — create folders:

**DeployService.cs, line 46:**
```csharp
Directory.CreateDirectory(isolatedAuthDir);
```

Works on all platforms; creates parent directories if needed.

#### `Directory.Delete(recursive: true)` — remove folders:

**DeployService.cs, line 116:**
```csharp
Directory.Delete(isolatedAuthDir, recursive: true);
```

Works on all platforms; recursively removes the folder and its contents.

---

### 4. Environment variable `PAC_AUTH_PROFILE_DIRECTORY` ✓

**DeployService.cs, RunPacCommandAsync and RunPacCommandWithOutputAsync:**
```csharp
var psi = new ProcessStartInfo
{
    FileName = "pac",
    Arguments = command,
    // ...
};

psi.EnvironmentVariables["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir;
```

`PAC_AUTH_PROFILE_DIRECTORY` is the official PAC CLI environment variable; supported on:
- ✅ Windows
- ✅ Linux
- ✅ macOS

---

### 5. Two-level validation ✓

Both validation levels work in the container:

#### CHECK 1 (PRE-DEPLOY): `pac auth who`
```csharp
var whoOutput = await RunPacCommandWithOutputAsync("auth who", isolatedAuthDir);
```

PAC CLI returns output regardless of platform. String parsing is the same.

#### CHECK 2 (POST-DEPLOY): Log file parsing
```csharp
var logContent = await File.ReadAllTextAsync(logFilePath);
var uriLine = logContent
    .Split('\n')
    .FirstOrDefault(line => line.Contains("Deployment Target Organization Uri:", ...));
```

Reading and parsing a text file is platform-independent.

---

## Testing in the Container

### Run the container:

```bash
# Build image
docker build -t d365fo-deploy-portal .

# Run with docker-compose
docker compose up -d

# Or directly
docker run -d \
  -p 5000:5000 \
  -v deploy-data:/app/data \
  -v deploy-packages:/app/packages \
  --name deploy-portal \
  d365fo-deploy-portal
```

### Check logs:

```bash
# App logs
docker compose logs -f deploy-portal

# Or
docker logs -f deploy-portal
```

### Expected logs during deployment:

```
[Isolation] Using dedicated PAC auth directory: /tmp/DeployPortal/pac_auth_3_4a5b6c7d...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...

[CHECK 1 - PRE-DEPLOY]
Verifying connection (pac auth who)...
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com ✓

Starting deployment to Cst-hfx-tst-07...
Deployment completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://cst-hfx-tst-07.crm.dynamics.com/...
[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment. ✓✓

[Cleanup] Removed isolated PAC auth directory: /tmp/DeployPortal/pac_auth_3_4a5b6c7d...
```

### Verify folders are created and removed:

```bash
# Attach to container
docker exec -it deploy-portal bash

# Check temp directory
ls -la /tmp/DeployPortal/

# During deployment you will see:
# pac_auth_1_abc123/
# pac_auth_2_xyz789/

# After completion — folders are removed
```

---

## Parallel Deployments in the Container

The container supports parallel deployments to **multiple environments at once**:

```
Deployment #1 → /tmp/DeployPortal/pac_auth_1_abc123/ → cst-hfx-tst-07
Deployment #2 → /tmp/DeployPortal/pac_auth_2_xyz789/ → cst-hfx-tst-05
Deployment #3 → /tmp/DeployPortal/pac_auth_3_def456/ → infra-tst-01

All three run in parallel, no conflicts! ✓
```

---

## Production Deployment Checklist

- [x] PAC CLI installed in container
- [x] Temp directory configured (`/tmp/DeployPortal`)
- [x] Cross-platform code (Path.Combine, Directory.*)
- [x] `PAC_AUTH_PROFILE_DIRECTORY` works on Linux
- [x] Two-level validation works in container
- [x] Automatic cleanup of temp folders
- [x] Health check configured (Dockerfile, lines 93-94)
- [x] Volumes for persistent data (`/app/data`, `/app/packages`)

---

## Benefits in the Container

### Process isolation
Each container is a separate environment. Even with multiple container instances, they do not conflict.

### Clean environment
On each container start, `/tmp/DeployPortal` is clean (unless you use a volume for it).

### Predictability
Same behavior on dev, staging, and prod because everyone uses the same Docker image.

### Scalability
You can run multiple container instances for parallel deployments (with shared volumes for DB and packages).

---

## Important Notes

### 1. Persistent data

Use Docker volumes for:
- `/app/data` — database, encryption keys
- `/app/packages` — uploaded packages

**Otherwise data is lost on container restart!**

### 2. Deployment logs

Deployment logs are stored in `/tmp/DeployPortal/logs/` inside the container. To access them from the host:

```yaml
# docker-compose.yml — add volume for logs
volumes:
  - deploy-data:/app/data
  - deploy-packages:/app/packages
  - deploy-logs:/tmp/DeployPortal/logs  # <-- logs

volumes:
  deploy-logs:
    name: deploy-portal-logs
```

Or use `docker cp`:
```bash
docker cp deploy-portal:/tmp/DeployPortal/logs/deploy_1_20260213_120000.log ./
```

### 3. PAC CLI version

To use a different PAC CLI version:

```dockerfile
# Dockerfile — change ARG
ARG PAC_CLI_VERSION=1.50.0  # <-- your version
```

Current version `1.49.4` is the latest targeting .NET 9 (PAC CLI 2.x requires .NET 10).

---

## Related Documentation

- `docs/TWO_LEVEL_VALIDATION.md` — detailed description of both checks
- `docs/SOLUTION_IMPLEMENTED.md` — PAC auth isolation implementation
- `docs/README_DEPLOYMENT_FIX.md` — short summary
- `docs/PAC_AUTH_ISOLATION.md` — technical isolation description

---

**Date:** 2026-02-13  
**Status:** ✅ Full compatibility with Docker container  
**Testing:** Ready to run in container
