# 📦 D365FO Deploy Portal — Release Notes v1.4.0

**Release Date:** February 13, 2026  
**Type:** Feature Release — Deployment History Archive

---

## ✨ New Features

### Deployment History Archive (Soft Delete)

Added comprehensive archive functionality for deployment history management. Instead of permanently deleting deployments, you can now **archive** them for safekeeping and restore them later if needed.

**Key Features:**

1. **Two-Tab Interface**
   - **Active Deployments** — shows all active (non-archived) deployment history
   - **Archive** — shows all archived deployments separately

2. **Soft Delete (Archive)**
   - Archive deployments from active history
   - Archived deployments are hidden from main view but retained in database
   - Track when deployment was archived (`ArchivedAt` timestamp)
   - All deployment data, logs, and links are preserved

3. **Restore from Archive**
   - Easily restore archived deployments back to active history
   - One-click restoration with confirmation dialog

4. **Permanent Deletion**
   - Delete archived deployments permanently (hard delete)
   - Clear warning with confirmation: "⚠️ THIS ACTION CANNOT BE UNDONE! ⚠️"
   - Removes deployment record, logs, and log files

5. **Independent Filters**
   - Separate filters for Active and Archive tabs
   - Filter by Package and/or Environment in each tab

**User Interface:**

**Active Deployments Tab:**
```
┌──────────────────────────────────────────────┐
│ 🕐 Active Deployments                        │
├──────────────────────────────────────────────┤
│ [View] [Archive] ← Actions per row          │
└──────────────────────────────────────────────┘
```

**Archive Tab:**
```
┌──────────────────────────────────────────────┐
│ 📦 Archive                                   │
├──────────────────────────────────────────────┤
│ [View] [Restore] [Delete Forever] ← Actions │
└──────────────────────────────────────────────┘
```

---

## 🔧 Technical Implementation

### Database Schema Changes

**New columns in `Deployments` table:**
```sql
IsArchived    INTEGER NOT NULL DEFAULT 0  -- Soft delete flag
ArchivedAt    TEXT NULL                   -- Archive timestamp
```

**New index for performance:**
```sql
CREATE INDEX IX_Deployments_IsArchived ON Deployments(IsArchived);
```

### Auto-Migration

The application automatically applies schema changes on startup:
- Existing deployments default to `IsArchived = false` (active)
- No manual migration required
- Backwards compatible with existing databases

### Updated Queries

All deployment queries now filter by archive status:
- **Home page** — shows only active deployments
- **Deployment History** — separate tabs for active vs archived
- **Package deletion check** — only considers active deployments

---

## 📄 Modified Files

**Models:**
- `src/DeployPortal/Models/Deployment.cs` — added `IsArchived` and `ArchivedAt` properties

**UI:**
- `src/DeployPortal/Components/Pages/Deployments.razor` — redesigned with tabs and archive actions
- `src/DeployPortal/Components/Pages/Home.razor` — filters archived deployments

**Services:**
- `src/DeployPortal/Services/PackageService.cs` — updated to ignore archived deployments

**Data:**
- `src/DeployPortal/Data/AppDbContext.cs` — added index for `IsArchived`
- `src/DeployPortal/Program.cs` — auto-migration for new columns
- `migrations/20260213_add_deployment_archive.sql` — SQL migration script

**Project:**
- `src/DeployPortal/DeployPortal.csproj` — version bump to 1.4.0

---

## 🎯 Use Cases

### Use Case 1: Clean Up Old Deployments
Archive old deployments to keep your active history clean and focused on recent activity.

### Use Case 2: Preserve Historical Data
Keep all deployment records for audit/compliance purposes without cluttering the main view.

### Use Case 3: Temporary Removal
Archive a deployment temporarily, then restore it later if needed.

### Use Case 4: Final Cleanup
After archiving, review archived deployments and permanently delete only what you no longer need.

---

## 📦 Installation & Upgrade

### Docker Users (recommended)
```bash
docker pull vglu/d365fo-deploy-portal:1.4.0

# Stop old container
docker stop deploy-portal
docker rm deploy-portal

# Start new container
docker run -d \
  --name deploy-portal \
  -p 8080:8080 \
  -v deploy-portal-data:/app/data \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e PAC_DISABLE_TELEMETRY=true \
  vglu/d365fo-deploy-portal:1.4.0
```

**⚠️ Database Migration:**
The application automatically adds the new columns on first startup. Your existing deployments will remain active (not archived).

### Manual Build
```bash
cd d:\Projects\D365FODeployPortal
git pull
dotnet restore
dotnet publish src/DeployPortal/DeployPortal.csproj -c Release -o ./publish
```

---

## 🚀 What's Next?

This is a feature release. For previous changes, see:
- [v1.3.3 Release Notes](RELEASE_NOTES_v1.3.3.md) — Package deletion UX improvements
- [v1.3.2 Release Notes](RELEASE_NOTES_v1.3.2.md) — FOREIGN KEY constraint fix
- [v1.3.1 Release Notes](RELEASE_NOTES_v1.3.1.md) — Interactive auth validation fix
- [v1.3.0 Release Notes](RELEASE_NOTES_v1.3.0.md) — Major SOLID refactoring

---

## 📞 Support

If you encounter any issues, please:
1. Check the [documentation](docs/)
2. Review logs in `C:\Temp\DeployPortal\logs` (Windows) or `/tmp/DeployPortal/logs` (Docker)
3. Contact support: [vhlu@sims-service.com](mailto:vhlu@sims-service.com) — [Sims Tech](https://sims-service.com/)

---

## 🎉 Highlights

- ✅ **Non-destructive** — archive instead of delete
- ✅ **Reversible** — restore from archive anytime
- ✅ **Clean interface** — separate tabs for active vs archived
- ✅ **Automatic migration** — no manual database updates needed
- ✅ **Audit-friendly** — preserve all historical data
