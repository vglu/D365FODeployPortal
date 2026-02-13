# ✅ РЕШЕНИЕ РЕАЛИЗОВАНО: Изоляция PAC Auth

## Что было сделано

Реализован **Вариант 3: Изолированный PAC_AUTH_PROFILE_DIR** для гарантии подключения к правильному энвайронменту при наличии Service Principal с доступом к нескольким энвайронментам.

---

## Как это работает

### До изменений (проблемная ситуация):

```
PAC CLI использует глобальное хранилище auth profiles:
%USERPROFILE%\.pac\auth\

Deployment #1: pac auth create --environment cst-hfx-tst-07
               ↓
           создаёт profile1 в глобальной папке
               ↓
           но SP имеет доступ к нескольким энвам
               ↓
           PAC CLI выбирает c365afspmunified ❌
               ↓
           деплоймент идёт на НЕПРАВИЛЬНЫЙ энвайронмент!
```

### После изменений (решение с двойной проверкой):

```
Deployment #1 (cst-hfx-tst-07):
   isolatedAuthDir = C:\Temp\DeployPortal\pac_auth_1_abc123...
   PAC_AUTH_PROFILE_DIRECTORY = isolatedAuthDir
   ↓
   pac auth create → сохраняется ТОЛЬКО в pac_auth_1_abc123
   ↓
   [CHECK 1] pac auth who → валидация (проверяем что URL правильный)
   ↓ если неправильный → Exception ДО начала деплоя
   ↓ если правильный → продолжаем
   ↓
   pac package deploy → берёт auth ТОЛЬКО из pac_auth_1_abc123
   ↓
   [CHECK 2] парсим лог файл → проверяем "Deployment Target Organization Uri"
   ↓ если неправильный → Exception, деплоймент Failed
   ↓ если правильный → Success
   ↓
   деплоймент завершён на ПРАВИЛЬНОМ энвайронменте ✅✅
   ↓
   папка удаляется

Deployment #2 (параллельно на cst-hfx-tst-05):
   isolatedAuthDir = C:\Temp\DeployPortal\pac_auth_2_xyz789...
   PAC_AUTH_PROFILE_DIRECTORY = isolatedAuthDir
   ↓
   полностью изолирован от Deployment #1! ✅
```

---

## Изменённые файлы

### 1. `src/DeployPortal/Services/DeployService.cs`

**Добавлено:**
- ✅ Параметр `isolatedAuthDir` в `DeployPackageAsync`
- ✅ Установка `PAC_AUTH_PROFILE_DIRECTORY` для всех PAC CLI вызовов
- ✅ Новый метод `RunPacCommandWithOutputAsync` для захвата вывода
- ✅ Валидация через `pac auth who` после аутентификации
- ✅ Автоматическая очистка в `finally` блоке
- ✅ Детальные логи изоляции и валидации

**Ключевой код:**
```csharp
// Каждый PAC CLI процесс получает уникальную папку
psi.EnvironmentVariables["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir;

// Проверка что подключились к правильному энву
if (!whoOutput.Contains(environment.Url, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"PAC authentication verification FAILED! Expected: {environment.Url}");
}
```

### 2. `src/DeployPortal/Services/DeploymentOrchestrator.cs`

**Добавлено:**
- ✅ Создание уникального `isolatedAuthDir` для каждого деплоймента
- ✅ Передача `isolatedAuthDir` в `DeployPackageAsync`

**Код:**
```csharp
var isolatedAuthDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");

await deployService.DeployPackageAsync(
    deployment.Environment,
    deployDir,
    logFilePath,
    isolatedAuthDir,  // ← изолированная папка
    msg => Log(msg));
```

---

## Преимущества решения

### ✅ Полная изоляция
Каждый деплоймент работает со своим auth profile. Никакого глобального состояния.

### ✅ Параллельные деплойменты
Можно деплоить на несколько энвайронментов одновременно без конфликтов:
```
Deployment #1 → Cst-hfx-tst-07 (параллельно)
Deployment #2 → CST-HFX-TST-05 (параллельно)
Deployment #3 → Infra-tst-01 (параллельно)
```

### ✅ Двухуровневая валидация

**Уровень 1 (PRE-DEPLOY): Проверка `pac auth who`**
- Выполняется **ДО** начала деплоймента
- Если неправильный энв → Exception **БЕЗ применения пакета**
- Быстрая проверка (< 1 секунды)

**Уровень 2 (POST-DEPLOY): Проверка лог файла**
- Выполняется **ПОСЛЕ** деплоймента
- Парсит "Deployment Target Organization Uri" из лога
- Финальное подтверждение что пакет применён на правильном энве
- Если неправильный → деплоймент помечается Failed с детальным сообщением

### ✅ Автоматическая очистка
Папка `pac_auth_*` удаляется после деплоймента (даже если был Exception).

### ✅ Независимость от глобального состояния
Не важно:
- Кто и когда запускал `pac auth` вручную
- Есть ли старые auth profiles в `%USERPROFILE%\.pac\auth`
- Какой auth был активным до запуска

---

## Что будет в логах

При следующем деплойменте вы увидите **двухуровневую валидацию**:

```
[Isolation] Using dedicated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_4a5b6c7d8e9f...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...
Verifying connection (pac auth who)...
Connection verified.

[CHECK 1 - PRE-DEPLOY]
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com ✓

Starting deployment to Cst-hfx-tst-07...
Package: D:\Temp\...\TemplatePackage.dll
...
(деплоймент идёт...)
...
Deployment to Cst-hfx-tst-07 completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://cst-hfx-tst-07.crm.dynamics.com/XRMServices/...
[Post-Deploy Validation] ✓ Matches expected environment: cst-hfx-tst-07.crm.dynamics.com
[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment.

[Cleanup] Removed isolated PAC auth directory: C:\Temp\DeployPortal\pac_auth_3_4a5b6c7d8e9f...
```

### Если что-то пойдёт не так:

**Сценарий 1: PAC auth подключился к неправильному энву (CHECK 1 провален):**
```
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com
Connection verified.
[ERROR] PAC authentication verification FAILED! 
        Expected environment: cst-hfx-tst-07.crm.dynamics.com, 
        but 'pac auth who' returned different environment.

Deployment FAILED (ДО начала деплоя — ничего не применено) ✓
```

**Сценарий 2: Деплоймент пошёл на неправильный энв (CHECK 2 провален):**
```
Deployment to Cst-hfx-tst-07 completed.
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://c365afspmunified.crm.dynamics.com/...
[ERROR] ❌ POST-DEPLOYMENT VALIDATION FAILED! ❌
        Package was deployed to WRONG environment!
        Expected: cst-hfx-tst-07.crm.dynamics.com
        Actual: c365afspmunified.crm.dynamics.com

Deployment marked as FAILED (но пакет УЖЕ применён на неправильном энве) ⚠️
```

Второй сценарий маловероятен если первая проверка прошла, но добавляет дополнительную уверенность.

---

## Тестирование

### Шаг 1: Запустите деплоймент
1. Откройте портал: http://localhost:5137/deploy
2. Выберите пакет и environment "Cst-hfx-tst-07"
3. Запустите деплоймент

### Шаг 2: Проверьте логи
1. В UI портала смотрите реалтайм логи
2. Найдите строку `[Isolation] Using dedicated PAC auth directory:`
3. Найдите строку `[Validation] Confirmed connected to correct environment:`

### Шаг 3: Проверьте итоговый лог файл
```powershell
# После завершения деплоймента
$logPath = "C:\Temp\DeployPortal\logs\deploy_X_YYYYMMDD_HHMMSS.log"
Select-String "Deployment Target Organization Uri" $logPath
```

Должно быть:
```
Deployment Target Organization Uri: https://cst-hfx-tst-07.crm.dynamics.com/...
```

### Шаг 4: Тест параллельных деплойментов
Запустите два деплоймента одновременно на разные энвайронменты и убедитесь что оба работают корректно.

---

## Документация

Полная документация сохранена в:
- `docs/PAC_AUTH_ISOLATION.md` — техническое описание решения

---

## Статус

- ✅ Код реализован
- ✅ Компиляция успешна
- ✅ Linter ошибок нет
- ⏳ Требуется тестирование на реальном деплойменте

---

Дата реализации: 2026-02-13  
Причина: Решение проблемы деплойментов #1 и #2 на неправильный энвайронмент
