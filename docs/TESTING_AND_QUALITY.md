# Тестирование, покрытие кода, SOLID и E2E

## Текущее покрытие тестами

После запуска с `coverlet.runsettings` (см. ниже):

| Метрика | Значение |
|--------|----------|
| **Строки (все проекты)** | **~25%** (1811 / 7264) |
| **Ветвления (branches)** | ~16% |
| **DeployPortal (основное приложение)** | ~18% line rate |

Покрытие дают в основном:
- **ApiTests** — HTTP API (packages, environments, deploy, refresh-licenses и т.д.), поднимается тестовый хост;
- **UnitTests** — PackageService (конвертация, refresh licenses) с in-memory DB и моками;
- **PackageContentServicesUnitTests** — IPackageContentService / IPackageModificationService;
- **DeploymentServicesUnitTests** — DeployService и валидаторы (моки PAC CLI);
- **IntegrationTests** — конвертация LCS↔Unified на реальных zip (без внешних сервисов).

Не покрыты или покрыты слабо: большая часть `Program.cs` (маршруты и хендлеры), UI (Razor), часть сервисов (SettingsService, MergeService, AzureDevOps*, CredentialParser, SecretProtectionService), PackageOps (ConvertEngine, MergeEngine, PackageAnalyzer).

---

## Что максимизировать в первую очередь

Целесообразно поднимать покрытие там, где выше риски и частота изменений:

1. **Критичные сценарии (по возможности к 80%+ по сценариям)**  
   - Загрузка/сохранение пакетов, конвертация LCS↔Unified, merge.  
   - Обновление метаданных пакета и запись в changelog (UpdatePackageAsync, LogChangeAsync).  
   - Добавление/удаление моделей и лицензий (PackageModificationService).  
   - Деплой: оркестратор, валидаторы, изоляция папок (уже частично покрыты юнитами).

2. **API (уже хорошо трогают ApiTests)**  
   - Держать и дополнять тесты на все публичные эндпоинты (packages, environments, deploy, licenses, refresh-licenses и т.д.).

3. **Чистая логика без I/O**  
   - PackageAnalyzer (детекция типа пакета, имён модулей, лицензий).  
   - CredentialParser, ExceptionHelper.  
   - Валидаторы деплоя (PreDeployAuthValidator, PostDeployLogValidator) — уже есть юниты.

4. **Не гнаться за 100% по всему коду**  
   - UI (Razor/Blazor) обычно покрывают E2E и выборочно.  
   - `Program.cs` (маппинг роутов) достаточно покрывать через ApiTests и E2E.  
   - Вспомогательные/редко меняющиеся куски можно оставить с низким покрытием, если сценарии критичного пути закрыты.

Итого: реалистичная цель — **поднять общее покрытие до 40–50%** за счёт сервисов пакетов, деплоя и API; критичные сценарии по возможности довести до **80%+ по ветвлениям** в этих модулях.

---

## SOLID — что уже сделано и что можно усилить

### Уже сделано (Deployment)

- **SRP**: DeployService — оркестратор; PacAuthService, PacDeploymentService, валидаторы, IsolatedDirectoryManager — отдельные обязанности.  
- **OCP**: Валидаторы реализуют `IDeploymentValidator`, добавление новых проверок без изменения оркестратора.  
- **DIP/ISP**: Интерфейсы `IDeployService`, `IPacAuthService`, `IPacDeploymentService`, `IPacCliExecutor`, `IIsolatedDirectoryManager`, `IDeploymentValidator` — всё мокается в тестах.

Подробно: `docs/SOLID_REFACTORING_PLAN.md`, `docs/SOLID_REFACTORING_COMPLETE.md`.

### Что можно довести до «максимально возможного»

1. **PackageService**  
   - Сейчас один большой класс (upload, convert, merge, licenses, metadata).  
   - Можно выделить в отдельные сервисы, например:  
     - «Только конвертация/merge» (уже есть IConvertService),  
     - «Только работа с лицензиями внутри пакета» (частично вынесено в IPackageChangeLogService, PackageModificationService).  
   - Дальше: вынести «только сохранение/чтение метаданных пакета в БД» в отдельный слой/сервис и зависеть от интерфейса — проще тесты и замена реализации.

2. **SettingsService, SecretProtectionService**  
   - Ввести интерфейсы (например `ISettingsService`, `ISecretProtectionService`) и внедрять их в сервисы, которые сейчас зависят от конкретных классов.  
   - Это даст DIP и возможность мокать в тестах без реальных файлов и защиты секретов.

3. **MergeService, AzureDevOpsBuildService / AzureDevOpsReleaseService**  
   - Если будут использоваться из нескольких мест или появятся альтернативные реализации — выделить интерфейсы и зависеть от абстракций.  
   - Для текущего объёма можно оставить как есть и усиливать тестами (в т.ч. интеграционными).

4. **Program.cs**  
   - Огромный файл с роутами и минимальной логикой.  
   - Имеет смысл выносить группы эндпоинтов в extension-методы или отдельные классы (например `PackagesApi`, `EnvironmentsApi`, `DeployApi`) — так проще читать и точечно тестировать через ApiTests.

Итого по SOLID: **деплой уже приведён к SOLID; остальное — по мере необходимости** (интерфейсы под тесты и новые фичи). «100% SOLID везде» не обязательно — достаточно критичные и растущие модули.

---

## E2E тесты

### Что уже есть

- **Playwright** (`Microsoft.Playwright.NUnit`) в том же проекте `DeployPortal.Tests`.  
- Класс **E2ETests**: поднимается приложение (`dotnet run`), подключается браузер, проверяются страницы и сценарии.  
- Сценарии: главная (Dashboard), навигация (Packages, Environments, Deploy, Deployments, Settings), загрузка пакета, переход в детали пакета и т.д.

### Как запускать

- **Обычные тесты (без E2E):**  
  `.\run-tests.ps1`  
  или  
  `dotnet test src\DeployPortal.Tests\DeployPortal.Tests.csproj --filter "FullyQualifiedName!~E2ETests"`

- **С включением E2E:**  
  `.\run-tests.ps1 -IncludeE2E`  
  или  
  `dotnet test src\DeployPortal.Tests\DeployPortal.Tests.csproj --filter "FullyQualifiedName!~ConvertRealLcsPackage_FromEnv"`  
  (E2E при этом выполняются; тяжёлый интеграционный тест с реальным пакетом из окружения по-прежнему можно исключать отдельным фильтром).

Требования для E2E: установленный Playwright (например `pwsh bin/Debug/net9.0/playwright.ps1 install` или через `dotnet test` после первого запуска с E2E). Приложение поднимается на `http://localhost:5199` с тестовой БД и путём хранения пакетов во временных каталогах.

### Что можно добавить в E2E

- Переход в детали пакета (Packages → клик по пакету → PackageDetails).  
- Вкладки Models / Licenses / Changelog: наличие блоков, кнопки Add, открытие диалога просмотра лицензии (и что внутри — форматированный XML).  
- Сценарий: загрузка пакета → открытие деталей → добавление лицензии (или модели) через диалог → проверка, что в списке появилась запись и (по возможности) что changelog обновился.  
- Deploy: выбор окружения и пакета, нажатие Deploy (без реального PAC можно проверять до отправки или мокать API).  
- Settings: открытие страницы, проверка наличия ключевых секций (без обязательного сохранения).

Так можно постепенно расширять E2E без больших изменений архитектуры.

---

## Как считать покрытие

1. Убедиться, что в корне репозитория есть **coverlet.runsettings** (уже добавлен) с включением `[DeployPortal]*` и `[DeployPortal.PackageOps]*`.

2. Запуск тестов с сбором покрытия:  
   `.\run-tests.ps1 -Coverage`  
   или  
   `dotnet test src\DeployPortal.Tests\DeployPortal.Tests.csproj --filter "FullyQualifiedName!~E2ETests&FullyQualifiedName!~ConvertRealLcsPackage_FromEnv" --collect:"XPlat Code Coverage" --results-directory TestResults --settings coverlet.runsettings`

3. Отчёт: в `TestResults\<guid>\coverage.cobertura.xml` (и при необходимости `coverage.opencover.xml`).  
   В корне `coverage` атрибуты `line-rate` и `branch-rate` — проценты в формате 0.0–1.0 (например 0.2493 ≈ 25%).

4. Опционально: установить [ReportGenerator](https://github.com/danielpalme/ReportGenerator) и строить HTML:  
   `reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:Html`  
   затем открыть `coveragereport/index.html`.

---

## Краткие ответы на вопросы

| Вопрос | Ответ |
|--------|--------|
| На сколько процентов покрыт код? | **~25% строк** по решению в целом, **~18%** по проекту DeployPortal. |
| Что максимизировать? | Критичные сценарии (пакеты, конвертация, merge, деплой, API) — к 80%+ по сценариям; общее покрытие — к 40–50%. |
| SOLID — сделать по максимуму? | Деплой уже приведён к SOLID. Остальное — вынести интерфейсы под тесты (Settings, SecretProtection, при необходимости Package/Merge/AzureDevOps) и разбить крупный Program/сервисы при росте кода. |
| Можно ли добавить/вызывать E2E? | Да. E2E уже есть (Playwright), запуск: `.\run-tests.ps1 -IncludeE2E`. Можно расширять сценариями: детали пакета, модели/лицензии/changelog, просмотр лицензии, deploy flow, settings. |
