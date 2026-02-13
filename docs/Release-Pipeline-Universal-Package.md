# Deploy via Release Pipeline (Universal Package)

Инструкция по настройке Azure DevOps Release Pipeline для загрузки пакета в Artifacts (Universal Package) и запуска релиза из Deploy Portal.

## Что нужно

- **Azure DevOps:** организация, проект, PAT с правами **Release** (Read, write & manage) и **Packaging** (Read & write).
- **Azure CLI (az)** с расширением `azure-devops` (устанавливается при первом использовании).
- В Release Pipeline должен быть артефакт типа **Universal package**, а не только Build.

---

## 1. Настройка артефакта в определении релиза

Если в определении релиза артефакт только типа **Build**, релиз не сможет использовать загруженный Universal Package. Нужно добавить артефакт типа **Azure Artifacts (Universal)**.

### Шаги в Azure DevOps

1. **Pipelines → Releases** → выберите нужное определение (например, ClientSTeam-HFX-supporttest01).
2. **Edit** → вкладка **Artifacts** → **Add** (Добавить артефакт).
3. **Source type:** выберите **Azure Artifacts**.
4. Заполните:
   - **Feed\***: `PPackages` (или ваш фид).
   - **Package type:** `Universal`.
   - **Package\***: выберите пакет из списка (например, `axdeployablepackagepcm_2026.2.11.1` — имя в lowercase).
   - **Default version:** **Specify at the time of release creation** (указывать при создании релиза).
   - **Source alias\***: например `_universal-package` или `_upack` (этот alias нужно будет выбирать в Deploy Portal).
5. **Add** → **Save** определение.

Артефакт типа Build можно оставить; для запуска из портала выбирайте в диалоге артефакт с alias Universal package.

---

## 2. Настройки в Deploy Portal

- **Settings** → укажите **Azure DevOps organization**, **project** и при необходимости **PAT** (сохраняется зашифрованно).
- На странице **Deploy** выберите пакет и нажмите **Release Pipeline**.
- В диалоге:
  - Шаг 1: введите/подтвердите org, project, PAT → загрузите список пайплайнов → выберите определение.
  - Шаг 2: выберите **артефакт** (alias) — тот, что типа Universal package (например, `_universal-package`).
  - **Feed name:** выберите из списка (например, PPackages) или «Other» и введите имя.
  - **Universal package name** и **Version** подставляются автоматически (имя в lowercase уходит в Artifacts).

Портал загружает пакет в Artifacts через `az artifacts universal publish` (с PAT через `AZURE_DEVOPS_EXT_PAT`), затем создаёт релиз через API, передавая выбранный alias и версию.

---

## 3. Скрипт из командной строки

Скрипт `scripts/upload-universal-package-from-portal.ps1` повторяет те же шаги (параметры берёт из запущенного портала или из настроек/БД).

### Запуск (параметры с портала, PAT передаётся вручную)

```powershell
.\scripts\upload-universal-package-from-portal.ps1 -Feed PPackages -Pat "ваш_PAT"
```

Если портал запущен (например, http://localhost:5137), скрипт подставит org, project, feed, путь к пакету и имя из API портала. Фид можно переопределить: `-Feed PPackages`. Имя пакета в Artifacts автоматически приводится к lowercase.

### Только вывод команды (без вызова az)

```powershell
.\scripts\upload-universal-package-from-portal.ps1 -NoRun
```

### Пример с явными параметрами

```powershell
.\scripts\upload-universal-package-from-portal.ps1 -Feed PPackages -PackagePath "C:\DeployPortal\Packages\package.zip" -PackageName "AXDeployablePackagePCM_2026.2.11.1" -Version "1.0.1234567890" -Pat "ваш_PAT"
```

### Загрузка и запуск релиза (создание release после publish)

```powershell
.\scripts\upload-universal-package-from-portal.ps1 -Feed PPackages -Pat "ваш_PAT" -DefinitionId 28 -ArtifactAlias "_universal-package"
```

**DefinitionId** — ID определения релиза (в URL при редактировании: `definitionId=28`). **ArtifactAlias** — alias артефакта типа Universal package в определении (например, `_universal-package`).

Если в определении есть **другие артефакты** с настройкой «Specify at the time of release creation» (например, Build), скрипт сам подставляет для них версию: для Build — последний успешный билд. Если автоматически не получится, передайте версии вручную:

```powershell
.\scripts\upload-universal-package-from-portal.ps1 -DefinitionId 28 -ArtifactAlias "_universal-package" -Pat "..." -AdditionalArtifactVersions @{ "_build-pipeline" = "12345" }
```

Здесь `12345` — ID билда для артефакта с alias `_build-pipeline`.

### Требования к имени пакета

Azure Artifacts Universal принимает только **lowercase**. Портал и скрипт автоматически приводят имя к нижнему регистру при публикации (в определении релиза и в фиде пакет будет отображаться как `axdeployablepackagepcm_2026.2.11.1` и т.п.).

---

## 4. Частые проблемы

| Проблема | Решение |
|---------|--------|
| «Feed 'Packages' doesn't exist» | Указать правильное имя фида (например, `PPackages`): в портале выбрать из списка или в скрипте `-Feed PPackages`. |
| «The package name provided is invalid» | Имя пакета должно быть в lowercase — портал/скрипт делают это автоматически; при ручном вызове `az` тоже используйте lowercase. |
| «The user is not authorized» | Использовать PAT (в портале — в диалоге или в настройках; в скрипте — `-Pat "..."` или переменная `AZURE_DEVOPS_EXT_PAT`). |
| Релиз создаётся, но не использует пакет | В определении релиза должен быть артефакт типа **Azure Artifacts / Universal package** с **Default version** = «Specify at the time of release creation». В портале выбирать именно этот артефакт (его alias). |
| «Cannot retrieve the default version for artifact '_build-pipeline'... selectDuringReleaseCreationType» | В определении есть ещё артефакты (например, Build) с «Specify at release creation». Скрипт сам подставляет для Build последний успешный билд; при ошибке передайте вручную: `-AdditionalArtifactVersions @{ "_build-pipeline" = "12345" }` (12345 — ID билда). |
| «Do you want to install the extension azure-devops» | Установить: `az extension add --name azure-devops --yes`. |

---

## 5. Краткая схема

1. **Azure DevOps:** в определении релиза добавлен артефакт Universal package (Feed, Package, alias, Default version = at release creation).
2. **Портал:** Deploy → Release Pipeline → выбор пайплайна и артефакта (alias Universal package) → Feed, имя пакета, версия → Upload and Start Release.
3. **Скрипт:** при необходимости те же шаги из CLI с `-Feed` и `-Pat`.

После успешной загрузки пакет появляется в фиде (Artifacts), релиз создаётся с указанной версией и при необходимости разворачивается по окружениям по правилам определения.
