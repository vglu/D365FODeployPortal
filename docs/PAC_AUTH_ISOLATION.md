# Изоляция PAC Auth для параллельных деплойментов

## Проблема

При использовании Service Principal с доступом к нескольким энвайронментам, PAC CLI мог выбирать неправильный энвайронмент из-за:
1. Глобального кеша auth profiles в `%USERPROFILE%\.pac\auth`
2. Переиспользования существующих auth сессий
3. Race conditions при параллельных деплойментах

**Результат:** Деплойменты #1 и #2 попали на `c365afspmunified.crm.dynamics.com` вместо `cst-hfx-tst-07.crm.dynamics.com`

---

## Решение

Использование изолированного PAC auth directory для каждого деплоймента через environment variable `PAC_AUTH_PROFILE_DIRECTORY`.

### Что изменилось:

#### 1. `DeployService.cs`

**Новый параметр в `DeployPackageAsync`:**
```csharp
public async Task DeployPackageAsync(
    Models.Environment environment,
    string unifiedPackageDir,
    string logFilePath,
    string isolatedAuthDir,  // ← НОВЫЙ параметр
    Action<string>? onLog = null)
```

**Ключевые изменения:**

1. **Создание изолированной папки auth:**
   ```csharp
   Directory.CreateDirectory(isolatedAuthDir);
   ```

2. **Передача `PAC_AUTH_PROFILE_DIRECTORY` в каждый PAC CLI вызов:**
   ```csharp
   psi.EnvironmentVariables["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir;
   ```

3. **Валидация подключения к правильному энвайронменту:**
   ```csharp
   var whoOutput = await RunPacCommandWithOutputAsync("auth who", isolatedAuthDir);
   if (!whoOutput.Contains(environment.Url, StringComparison.OrdinalIgnoreCase))
   {
       throw new InvalidOperationException(
           $"PAC authentication verification FAILED! Expected: {environment.Url}");
   }
   ```

4. **Автоматическая очистка в `finally` блоке:**
   ```csharp
   finally
   {
       Directory.Delete(isolatedAuthDir, recursive: true);
   }
   ```

#### 2. `DeploymentOrchestrator.cs`

**Создание уникального auth directory для каждого деплоймента:**
```csharp
var isolatedAuthDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");

await deployService.DeployPackageAsync(
    deployment.Environment,
    deployDir,
    logFilePath,
    isolatedAuthDir,  // ← передаём изолированную папку
    msg => Log(msg));
```

---

## Преимущества

✅ **Полная изоляция** — каждый деплоймент видит только свой auth profile  
✅ **Параллельные деплойменты** — можно деплоить на разные энвайронменты одновременно  
✅ **Нет race conditions** — никакого конфликта между потоками  
✅ **Автоматическая очистка** — папка удаляется после деплоймента (даже при ошибке)  
✅ **Валидация энвайронмента** — проверка что подключились к правильному энву  
✅ **Независимость от глобального состояния** — не важно кто и когда запускал `pac auth` вручную  

---

## Пример работы

### Deployment #1 (на Cst-hfx-tst-07):
```
1. Создаёт: C:\Temp\DeployPortal\pac_auth_1_a1b2c3d4e5f6...
2. Запускает: pac auth create --applicationId xxx --environment cst-hfx-tst-07.crm.dynamics.com
   с env var: PAC_AUTH_PROFILE_DIRECTORY=C:\Temp\DeployPortal\pac_auth_1_a1b2c3d4e5f6...
3. Auth profile сохраняется ТОЛЬКО в эту папку
4. Валидирует через pac auth who
5. Деплоит пакет (PAC CLI берёт auth ТОЛЬКО из этой папки)
6. Удаляет папку
```

### Deployment #2 (параллельно на CST-HFX-TST-05):
```
1. Создаёт: C:\Temp\DeployPortal\pac_auth_2_x9y8z7w6v5u4...
2. Запускает: pac auth create --applicationId yyy --environment cst-hfx-tst-05.crm.dynamics.com
   с env var: PAC_AUTH_PROFILE_DIRECTORY=C:\Temp\DeployPortal\pac_auth_2_x9y8z7w6v5u4...
3. Auth profile сохраняется ТОЛЬКО в эту папку (не пересекается с #1!)
4. Валидирует через pac auth who
5. Деплоит пакет (PAC CLI берёт auth ТОЛЬКО из этой папки)
6. Удаляет папку
```

**Результат:** Никакого конфликта! Каждый деплоймент работает в полной изоляции.

---

## Тестирование

### Проверка изоляции:

1. Запустите два деплоймента параллельно на разные энвайронменты
2. В логах увидите:
   ```
   [Isolation] Using dedicated PAC auth directory: C:\Temp\DeployPortal\pac_auth_1_...
   [Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com
   ```
3. После завершения проверьте что папки `pac_auth_*` удалены
4. В логах деплоймента проверьте:
   ```
   Deployment Target Organization Uri: https://cst-hfx-tst-07.crm.dynamics.com/
   ```

### При ошибке валидации:

Если Service Principal подключится к неправильному энвайронменту, деплоймент упадёт с сообщением:
```
PAC authentication verification FAILED! 
Expected environment: cst-hfx-tst-07.crm.dynamics.com, 
but 'pac auth who' returned different environment.
```

---

## Рекомендации

1. **Service Principal должен иметь доступ ТОЛЬКО к одному энвайронменту** — это самый надёжный подход
2. **Если SP имеет доступ к нескольким энвам** — теперь изоляция гарантирует правильный выбор
3. **Всегда проверяйте логи деплоймента** на наличие `Deployment Target Organization Uri`

---

## Изменённые файлы

- `src/DeployPortal/Services/DeployService.cs`
  - Добавлен параметр `isolatedAuthDir`
  - Добавлена установка `PAC_AUTH_PROFILE_DIRECTORY`
  - Добавлена валидация через `pac auth who`
  - Добавлена автоматическая очистка в `finally`
  - Добавлен метод `RunPacCommandWithOutputAsync`

- `src/DeployPortal/Services/DeploymentOrchestrator.cs`
  - Создание уникального `isolatedAuthDir` для каждого деплоймента
  - Передача `isolatedAuthDir` в `DeployPackageAsync`

---

Дата реализации: 2026-02-13  
Причина: Решение проблемы с деплойментами #1 и #2 на неправильный энвайронмент
