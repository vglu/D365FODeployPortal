# GitHub Releases and Packages

This repository is set up for **GitHub Releases** and publishing a **Docker image** to **GitHub Container Registry** (Packages).

## How It Works

When you push a tag matching `v*` (e.g. `v1.5.0`), the workflow [.github/workflows/release.yml](../.github/workflows/release.yml) runs:

1. **Windows build** — Publishes a self-contained app (win-x64) and packs it into a ZIP.
2. **Docker build** — Builds the image and pushes it to **GitHub Container Registry** (`ghcr.io`) and to **Docker Hub** (`vglu/d365fo-deploy-portal`). For Docker Hub, add repo secrets: `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`.
3. **Release** — Creates a release on the Releases tab with the ZIP attached and body text from `RELEASE_NOTES_vX.Y.Z.md` (if the file exists).

## Creating a Release

1. Ensure the version in `src/DeployPortal/DeployPortal.csproj` matches the tag you plan to use (e.g. `1.4.0`).
2. Create or update the release notes file: `RELEASE_NOTES_v1.4.0.md`.
3. Commit and push your changes.
4. Create and push the tag:

   ```bash
   git tag v1.4.0
   git push origin v1.4.0
   ```

5. The workflow will appear under **Actions**; after it completes successfully:
   - A new release with the ZIP will appear on the **Releases** tab;
   - The image `d365fo-deploy-portal` will appear under **Packages** (or in your profile).

## Releases (Artifacts)

- Page: `https://github.com/vglu/D365FODeployPortal/releases`
- Each release includes an archive: `DeployPortal-<version>-win-x64.zip` (extract and run `start.cmd` or `DeployPortal.exe`).
- Release body is taken from `RELEASE_NOTES_v<version>.md`; if the file is missing, a short default description is used.

## Packages (Docker on GHCR)

- Image: **GitHub Container Registry** — `ghcr.io/vglu/d365fo-deploy-portal`
- Tags: Git tag (e.g. `v1.4.0`) and `latest`.

Examples:

```bash
# Run web UI
docker run -p 5000:5000 -v deploy-data:/app/data -v deploy-packages:/app/packages ghcr.io/vglu/d365fo-deploy-portal:latest

# CLI conversion LCS → Unified
docker run --rm -v /path/to/packages:/data ghcr.io/vglu/d365fo-deploy-portal:latest convert /data/MyLcs.zip
```

Pulling from GHCR may require login the first time:

```bash
echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin
```

For public images, pull often works without login.

## Manual First Release (Optional)

If you need to create the very first release by hand (without a tag):

1. Go to **Releases** → **Create a new release**.
2. Choose a tag (create e.g. `v1.4.0`) or an existing commit.
3. Set the title, e.g. `v1.4.0`.
4. Paste the contents of `RELEASE_NOTES_v1.4.0.md` into the description.
5. Attach the ZIP manually (build with `.\publish.ps1` and zip the `publish/` folder).

For subsequent releases, using tags and the automated workflow is recommended.
