# 📦 D365FO Deploy Portal — Release Notes v1.5.0

**Release Date:** February 13, 2026  
**Type:** Feature Release — SOLID refactoring, tests, E2E

---

## ✨ New Features & Improvements

### SOLID: Interfaces for Settings and Secret Protection

- **`ISettingsService`** — все операции настроек (ValidateTools, GetEffectivePacPath, GetEffectiveModelUtilPath, GetAllSettings, SaveSettings, SaveAzureDevOpsSettings, SaveReleasePipelineFeedName) вынесены в интерфейс; `SettingsService` реализует его.
- **`ISecretProtectionService`** — интерфейс для шифрования/маскирования секретов (Encrypt, Decrypt, MaskSecret); `SecretProtectionService` реализует его.
- Везде в приложении (сервисы, страницы, DeploymentOrchestrator) используются интерфейсы через DI; упрощается тестирование и замена реализаций.

### Unit-тесты

- **PackageAnalyzer** — тесты для DetectPackageType (Unified, LCS, Other), ExtractModuleName, DetectMergeStrategy, DetectLicenseFiles.
- **CredentialParser** — тесты для Parse (ApplicationId, TenantId, Environments, невалидные URL отфильтровываются).
- **PackageService.UpdatePackageAsync** — проверка записи изменений Name и TicketUrl в changelog через `IPackageChangeLogService`.

### E2E (Playwright)

- Новый тест **PackageDetails_Navigate_ShowsModelsLicensesChangelogTabs** — переход на страницу деталей пакета с `/packages`, проверка наличия вкладок Models, Licenses, Changelog.

### Документация

- **`docs/TESTING_AND_QUALITY.md`** — описание покрытия тестами, рекомендации по SOLID, инструкции по запуску unit/E2E и покрытию (coverlet).
- **`coverlet.runsettings`** — настройки сбора покрытия для DeployPortal и DeployPortal.PackageOps.

---

## 🔧 Technical Details

### Modified / New Files

**Interfaces:**
- `src/DeployPortal/Services/ISettingsService.cs` (new)
- `src/DeployPortal/Services/ISecretProtectionService.cs` (new)

**Services:**
- `SettingsService.cs` — implements `ISettingsService`
- `SecretProtectionService.cs` — implements `ISecretProtectionService`, instance method `MaskSecret`
- `EnvironmentService.cs` — uses `ISecretProtectionService.MaskSecret`
- Все сервисы развёртки, конвертации, пакетов и окружений переведены на `ISettingsService` / `ISecretProtectionService`

**Tests:**
- `src/DeployPortal.Tests/PackageOps/PackageAnalyzerTests.cs` (new)
- `src/DeployPortal.Tests/CredentialParserTests.cs` (new)
- `src/DeployPortal.Tests/UnitTests.cs` — тест UpdatePackageAsync + changelog
- `src/DeployPortal.Tests/E2ETests.cs` — тест PackageDetails (вкладки Models, Licenses, Changelog)

**Config:**
- `Program.cs` — регистрация `ISettingsService`, `ISecretProtectionService`
- `coverlet.runsettings`, `run-tests.ps1` (поддержка `-Coverage`), `.gitignore` (coveragereport/)

**Project:**
- `src/DeployPortal/DeployPortal.csproj` — version 1.5.0

---

## 📦 Installation & Upgrade

### Docker (GitHub Container Registry)
```bash
docker pull ghcr.io/vglu/d365fo-deploy-portal:1.5.0
# or
docker pull ghcr.io/vglu/d365fo-deploy-portal:latest
```

### Docker Hub
```bash
docker pull vglu/d365fo-deploy-portal:1.5.0
docker pull vglu/d365fo-deploy-portal:latest
```

### Windows (ZIP from GitHub Release)
1. Скачайте `DeployPortal-1.5.0-win-x64.zip` со [страницы Releases](https://github.com/vglu/D365FODeployPortal/releases).
2. Распакуйте и запустите `start.cmd` или `DeployPortal.exe`.

---

## 🚀 What's Next?

- [v1.4.0 Release Notes](RELEASE_NOTES_v1.4.0.md) — Deployment History Archive
- [v1.3.3 Release Notes](RELEASE_NOTES_v1.3.3.md) — Package deletion UX

---

## 📞 Support

If you encounter any issues, please check the [documentation](docs/) or contact: [vhlu@sims-service.com](mailto:vhlu@sims-service.com) — [Sims Tech](https://sims-service.com/)
