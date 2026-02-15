# 📦 D365FO Deploy Portal — Release Notes v1.5.3

**Release Date:** February 2026  
**Type:** Feature Release — Deployment queue settings, delay between starts

---

## ✨ New Features & Improvements

### Deployment queue: configurable concurrency

- **Max concurrent deployments** — настраивается в **Settings** (блок «Deployment queue») и хранится в **базе данных** (таблица `AppSettings`). По умолчанию **2** (одновременно могут идти до двух деплойментов: один пакет в одно окружение каждый).
- Диапазон: 1–20. Изменение вступает в силу после перезапуска приложения.

### Delay between deployment starts

- Между **запусками** деплойментов соблюдается задержка **30 секунд**: первый деплой стартует сразу, каждый следующий — не ранее чем через 30 с после предыдущего старта. Снижает пиковую нагрузку при массовой постановке в очередь.

### Settings & database

- Добавлена сущность **`AppSetting`** и таблица **`AppSettings`** в БД (key-value). При первом запуске v1.5.3 таблица создаётся автоматически (миграция при старте).
- Остальные настройки по-прежнему хранятся в `usersettings.json`; только **MaxConcurrentDeployments** хранится в БД.

### Tests

- **SettingsService_MaxConcurrentDeployments_StoredInDatabase** — проверка чтения/записи значения из БД.
- **DeploymentOrchestrator_DelayBetweenStarts_IsThirtySeconds** — проверка константы задержки 30 с.
- Тесты, создающие `SettingsService`, обновлены под новый конструктор (передача `IDbContextFactory<AppDbContext>`).

---

## 🔧 Technical Details

### Modified / New Files

- `src/DeployPortal/Data/AppSetting.cs` (new)
- `src/DeployPortal/Data/AppDbContext.cs` — `DbSet<AppSetting>`, создание таблицы при старте
- `src/DeployPortal/Services/ISettingsService.cs` — свойство `MaxConcurrentDeployments`
- `src/DeployPortal/Services/SettingsService.cs` — чтение/запись из БД, константы 1–20, default 2
- `src/DeployPortal/Services/DeploymentOrchestrator.cs` — зависимость от `ISettingsService`, задержка 30 с между стартами, константа `DelayBetweenStartsSeconds`
- `src/DeployPortal/Components/Pages/Settings.razor` — блок «Deployment queue», поле Max concurrent deployments
- `src/DeployPortal/Program.cs` — создание таблицы `AppSettings`
- `src/DeployPortal.Tests/UnitTests.cs` — тест MaxConcurrentDeployments, передача `_dbFactory` в SettingsService
- `src/DeployPortal.Tests/Deployment/DeploymentServicesUnitTests.cs` — хелпер `CreateSettingsService`, тест задержки, `PooledDbContextFactory`

**Project:** `src/DeployPortal/DeployPortal.csproj` — version 1.5.3

---

## 📦 Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.5.3
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.5.3
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Скачайте `DeployPortal-1.5.3-win-x64.zip` со [страницы Releases](https://github.com/vglu/D365FODeployPortal/releases).
2. Распакуйте и запустите `start.cmd` или `DeployPortal.exe`.

---

## 🚀 What's Next?

- [v1.5.0 Release Notes](RELEASE_NOTES_v1.5.0.md) — SOLID refactoring, tests, E2E
- [v1.4.0 Release Notes](RELEASE_NOTES_v1.4.0.md) — Deployment History Archive

---

## 📞 Support

При вопросах и проблемах — создайте [Issue](https://github.com/vglu/D365FODeployPortal/issues) в репозитории.
