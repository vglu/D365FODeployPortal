# План реализации: Управление моделями и лицензиями в пакетах

**Ветка:** `feature/package-models-management`  
**Статус:** в работе

---

## Легенда
- ⬜ Не начато
- 🔄 В работе
- ✅ Сделано

---

## Этап 1: Модификация пакетов (PackageModificationService)

### 1.1 LCS / Merged

| # | Задача | Статус |
|---|--------|--------|
| 1.1.1 | `AddModelToLcsPackageAsync` — добавить .nupkg/.zip в AOSService/Packages/files/ | ✅ |
| 1.1.2 | `RemoveModelFromLcsPackageAsync` — удалить запись модели из ZIP | ✅ |
| 1.1.3 | `AddLicenseToLcsPackageAsync` — добавить файл в AOSService/Scripts/License/ | ✅ |
| 1.1.4 | `RemoveLicenseFromLcsPackageAsync` — удалить лицензию из ZIP | ✅ |

### 1.2 Unified

| # | Задача | Статус |
|---|--------|--------|
| 1.2.1 | `AddModelToUnifiedPackageAsync` — добавить *_managed.zip в корень пакета | ✅ |
| 1.2.2 | `RemoveModelFromUnifiedPackageAsync` — удалить *_managed.zip | ✅ |
| 1.2.3 | `AddLicenseToUnifiedPackageAsync` — добавить лицензию в _License_ внутри *_managed.zip | ✅ |
| 1.2.4 | `RemoveLicenseFromUnifiedPackageAsync` — удалить лицензию из _License_ | ✅ |

---

## Этап 2: UI

| # | Задача | Статус |
|---|--------|--------|
| 2.1 | Страница `/packages/{id}/details` с маршрутом и получением пакета | ✅ |
| 2.2 | Вкладка **Models**: таблица моделей, кнопки Add / Remove | ✅ |
| 2.3 | Вкладка **Licenses**: таблица лицензий, просмотр содержимого, Add / Remove | ✅ |
| 2.4 | Вкладка **Changelog**: таблица изменений по пакету | ✅ |
| 2.5 | Ссылка «Manage content» со страницы списка пакетов (Packages.razor) | ✅ |
| 2.6 | Диалоги: загрузка файла модели/лицензии (AddPackageFileDialog), подтверждение удаления | ✅ |

---

## Этап 3: Тесты

| # | Задача | Статус |
|---|--------|--------|
| 3.1 | Unit: PackageChangeLogService (логирование, история) | ✅ |
| 3.2 | Unit: PackageModificationService (ValidateModelRemovalAsync) | ✅ |
| 3.3 | Unit: PackageContentService (чтение) — опционально с тестовым ZIP | ⬜ |
| 3.4 | E2E: открыть пакет → просмотр Models/Licenses/Changelog | ⬜ |
| 3.5 | E2E: добавить/удалить модель или лицензию | ⬜ |

---

## Этап 4: Доработки и документация

| # | Задача | Статус |
|---|--------|--------|
| 4.1 | Обновить CONTINUATION_PACKAGE_MODELS.md (убрать TODO, отметить готово) | ✅ |
| 4.2 | При необходимости: правки манифестов (nuspec, manifest) | ⬜ |

---

## Итог выполнения

- **Этап 1:** Все 8 методов модификации пакетов (LCS + Unified) реализованы.
- **Этап 2:** UI: страница Package Details, вкладки Models / Licenses / Changelog, диалог загрузки файлов, ссылка «Manage content».
- **Этап 3:** Unit-тесты для PackageChangeLogService и PackageModificationService (валидация зависимостей). E2E — при необходимости добавить отдельно.
- **Этап 4:** План и документация обновлены.

*Последнее обновление: по мере выполнения*
