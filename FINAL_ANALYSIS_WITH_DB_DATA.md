# 🔍 ФИНАЛЬНЫЙ АНАЛИЗ: ПРОБЛЕМА С ДЕПЛОЙМЕНТАМИ #1 и #2

## ❌ ПРОБЛЕМА ПОДТВЕРЖДЕНА

Деплойменты **#1 и #2 попали на НЕПРАВИЛЬНЫЙ ЭНВАЙРОНМЕНТ!**

---

## 📊 ФАКТИЧЕСКИЕ ДАННЫЕ ИЗ БАЗЫ И ЛОГОВ

### Deployment #1 (Merged_20260211_180432)
**База данных говорит:**
- EnvironmentId: 6 (Cst-hfx-tst-07)
- Environment URL в БД: `cst-hfx-tst-07.crm.dynamics.com` ✅
- PackageId: 15
- Status: 4 (Completed)
- Started: 2026-02-11 18:06:41
- Completed: 2026-02-11 19:20:04
- Duration: ~1 час 13 минут

**Лог деплоймента показывает:**
```
Deployment Target Organization ID: 9f9541eb-43e2-ef11-b8e4-6045bd003904
Deployment Target Organization UniqueName: unq9f9541eb43e2ef11b8e46045bd003
Deployment Target Organization Uri: https://c365afspmunified.crm.dynamics.com/ ❌❌❌
Organization Version: 9.2.25123.166
```

### Deployment #2 (Merged_20260211_180432 Unified)
**База данных говорит:**
- EnvironmentId: 6 (Cst-hfx-tst-07)
- Environment URL в БД: `cst-hfx-tst-07.crm.dynamics.com` ✅
- PackageId: 16
- Status: 4 (Completed)
- Started: 2026-02-11 21:12:30
- Completed: 2026-02-11 22:20:44
- Duration: ~1 час 8 минут

**Лог деплоймента показывает:**
```
Deployment Target Organization ID: 9f9541eb-43e2-ef11-b8e4-6045bd003904
Deployment Target Organization UniqueName: unq9f9541eb43e2ef11b8e46045bd003
Deployment Target Organization Uri: https://c365afspmunified.crm.dynamics.com/ ❌❌❌
Organization Version: 9.2.25123.166
```

### Эталонный P2 (правильный деплоймент)
**Лог показывает:**
```
Deployment Target Organization ID: ef7d39e4-66d2-f011-8729-000d3a33a003 ✅
Deployment Target Organization UniqueName: unqef7d39e466d2f0118729000d3a33a ✅
Deployment Target Organization Uri: https://cst-hfx-tst-07.crm.dynamics.com/ ✅
Organization Version: 9.2.26014.142
```

---

## 🔍 КЛЮЧЕВОЕ РАЗЛИЧИЕ

| Параметр | Деплойменты #1 & #2 | Эталонный P2 | Совпадает? |
|----------|---------------------|--------------|------------|
| Organization ID | `9f9541eb-43e2-ef11-b8e4-6045bd003904` | `ef7d39e4-66d2-f011-8729-000d3a33a003` | ❌ НЕТ |
| Organization URL | `c365afspmunified.crm.dynamics.com` | `cst-hfx-tst-07.crm.dynamics.com` | ❌ НЕТ |
| Org Version | 9.2.25123.166 | 9.2.26014.142 | ❌ НЕТ |

**ВЫВОД: Это РАЗНЫЕ энвайронменты!**

---

## 🤔 ПОЧЕМУ ЭТО ПРОИЗОШЛО?

### Данные из базы Environment #6 (Cst-hfx-tst-07):
```
Id: 6
Name: Cst-hfx-tst-07
Url: cst-hfx-tst-07.crm.dynamics.com
TenantId: 77d6f5ce-824c-4d27-a315-4988ab78abb4
ApplicationId: cf5dcf5f-69fe-4855-b968-8c17c9c79030 ✅
ClientSecretEncrypted: CfDJ8PDETcEeYfVHteRH-3Bh6Qe8PJw... (есть) ✅
IsActive: 1
```

**Service Principal ПРИСУТСТВУЕТ!** 

Значит проблема НЕ в интерактивной аутентификации.

### Возможные причины:

1. **Service Principal имеет доступ к НЕСКОЛЬКИМ энвайронментам**
   - ApplicationId `cf5dcf5f-69fe-4855-b968-8c17c9c79030` зарегистрирован в Azure AD
   - Этот SP имеет права доступа к обоим энвайронментам:
     - `c365afspmunified.crm.dynamics.com`
     - `cst-hfx-tst-07.crm.dynamics.com`
   
2. **PAC CLI выбрал не тот энвайронмент**
   - При команде `pac auth create --environment "cst-hfx-tst-07.crm.dynamics.com"` PAC CLI подключился
   - Но по какой-то причине фактически выбрал другой энвайронмент
   
3. **Кеширование аутентификации**
   - Возможно была предыдущая сессия на `c365afspmunified` 
   - PAC CLI переиспользовал существующую сессию вместо создания новой

---

## 📋 ЧТО БЫЛО В ПАКЕТАХ

### Package #15 (Merged_20260211_180432)
```
Type: Merged
File: 20260211_180437_Merged_20260211_180432.zip
Size: 57,688,743 bytes (55 MB)
Parent: Package #11 (AXDeployablePackagePCM_2026.2.11.1)
Merged from: 
  - AXDeployablePackagePCM_2026.2.11.1 (LCS)
  - AXDeployablePackageAL_2026.2.11.1 (LCS)
  - AXDeployablePackageAFSPM_2026.2.11.1 (LCS)
Uploaded: 2026-02-11 18:04:39
```

### Package #16 (Merged_20260211_180432 Unified)
```
Type: Unified
File: 20260211_211216_Merged_20260211_180432_Unified.zip
Size: неизвестно (пакет удален)
Source: конвертирован из Package #15
Uploaded: 2026-02-11 21:12:17
```

**Оба пакета содержали одинаковые 19 FnO модулей** (видно из логов)

---

## ✅ РЕШЕНИЕ

### Вариант 1: Очистить кеш PAC CLI перед деплойментом

Добавьте в `DeployService.cs` очистку кеша auth перед созданием новой сессии:

```csharp
// Очистить все существующие auth перед созданием нового
await RunPacCommandAsync("auth clear", onLog);

// Затем создать новую auth
if (environment.HasServicePrincipal)
{
    await RunPacCommandAsync(
        $"auth create --applicationId {environment.ApplicationId} --clientSecret \"{clientSecret}\" --tenant {environment.TenantId} --environment {environment.Url}",
        onLog);
}
```

### Вариант 2: Использовать --name для auth профиля

Создавайте уникальные auth профили для каждого энвайронмента:

```csharp
var profileName = $"env_{environment.Id}_{environment.Name}";
await RunPacCommandAsync($"auth delete --name {profileName}", onLog); // удалить старый если есть
await RunPacCommandAsync(
    $"auth create --name {profileName} --applicationId {environment.ApplicationId} --clientSecret \"{clientSecret}\" --tenant {environment.TenantId} --environment {environment.Url}",
    onLog);
await RunPacCommandAsync($"auth select --name {profileName}", onLog); // выбрать
```

### Вариант 3: Проверить Organization ID после auth

После `pac auth who`, парсить Organization ID и сравнивать с ожидаемым:

```csharp
await RunPacCommandAsync("auth who", onLog);
// TODO: Parse output, extract Org ID, verify it matches expected environment
```

---

## 🎯 НЕМЕДЛЕННЫЕ ДЕЙСТВИЯ

1. ✅ Проверьте в Azure Portal права Service Principal `cf5dcf5f-69fe-4855-b968-8c17c9c79030`
   - К каким энвайронментам он имеет доступ?
   - Возможно нужен отдельный SP для каждого энвайронмента

2. ✅ Перед повторным деплойментом выполните:
   ```powershell
   pac auth clear
   pac auth list  # должен быть пустой
   ```

3. ✅ Добавьте в портал проверку Organization ID после аутентификации

4. ✅ Повторите деплоймент с очищенным кешем PAC CLI

---

## 📝 ЗАКЛЮЧЕНИЕ

**Корневая причина:** PAC CLI подключился к неправильному энвайронменту, несмотря на правильный URL в параметре `--environment`.

**Наиболее вероятная гипотеза:** Service Principal имеет доступ к нескольким энвайронментам, и PAC CLI выбрал не тот, возможно из-за кеширования предыдущей сессии.

**Пакеты были правильные**, проблема в аутентификации/выборе энвайронмента на уровне PAC CLI.

---

Дата анализа: 2026-02-13  
Инструменты: SQLite DB analysis + deployment logs comparison
