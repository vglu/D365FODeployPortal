# D365FO Deploy Portal

Web-based tool for deploying **Microsoft Dynamics 365 Finance & Operations** packages to Power Platform environments. Upload LCS/Unified packages, merge them, convert LCS→Unified, and deploy to one or multiple environments with real-time logs and full deployment history.

## What it does

- **Package management** — Upload LCS or Unified ZIP packages; auto-detect type; merge multiple LCS packages.
- **LCS → Unified conversion** — Built-in conversion during deployment (no external ModelUtil needed in container).
- **Multi-environment deploy** — Select a package and several targets; deploy to all at once.
- **Real-time logs** — Live deployment output in the browser (SignalR).
- **Deployment history** — Filter by package, environment, status, date.

## How to run

### Web UI (recommended)

```bash
docker run -d --name deploy-portal \
  -p 5000:5000 \
  -v deploy-data:/app/data \
  -v deploy-packages:/app/packages \
  vglu/d365fo-deploy-portal:latest
```

Open **http://localhost:5000**. Configure paths and environments in **Settings** and **Environments**, then use **Packages** and **Deploy**.

### Parameters and volumes

| Option | Meaning |
|--------|--------|
| `-p 5000:5000` | Expose web UI on port 5000 |
| `-v deploy-data:/app/data` | Persist database, keys, settings |
| `-v deploy-packages:/app/packages` | Persist uploaded packages |

### CLI conversion only (no web server)

One-off conversion of an LCS package to Unified format:

```bash
docker run --rm \
  -v /path/to/packages:/data \
  vglu/d365fo-deploy-portal:latest \
  convert /data/MyLcs.zip /data/MyUnified.zip
```

### Docker Compose

```bash
docker compose up -d
# Then open http://localhost:5000
```

## Tags

- `latest` — latest release
- `v1.x.x` — specific version (e.g. `v1.5.1`)

## Links

- **Source & docs:** [GitHub — vglu/D365FODeployPortal](https://github.com/vglu/D365FODeployPortal)
- **Releases (Windows ZIP):** [GitHub Releases](https://github.com/vglu/D365FODeployPortal/releases)
- **GHCR image:** `ghcr.io/vglu/d365fo-deploy-portal`
