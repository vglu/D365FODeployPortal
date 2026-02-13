# АНАЛИЗ ДЕПЛОЙМЕНТОВ #1 и #2 vs ЭТАЛОННЫЙ P2

## Сравнительная таблица

### Deployment #1 (Merged_20260211_180432)
- **Queued:** 2026-02-11 18:06:41
- **Started:** 2026-02-11 18:06:41
- **Completed:** 2026-02-11 19:20:04
- **Duration:** ~1 час 13 минут
- **Status:** Completed (4)
- **Source Path:** `D:\DEPLOYPORTAL\PACKAGES\20260211_180437_MERGED_20260211_180432_UNIFIED\PackageAssets`
- **Environment:** Cst-hfx-tst-07 (9f9541eb-43e2-ef11-b8e4-6045bd003904)
- **Deployment Target:** https://c365afspmunified.crm.dynamics.com/
- **Version Info:** 9.2.25123.166

### Deployment #2 (Merged_20260211_180432 (Unified))
- **Queued:** 2026-02-11 21:12:30
- **Started:** 2026-02-11 21:12:30
- **Completed:** 2026-02-11 22:20:44
- **Duration:** ~1 час 8 минут
- **Status:** Completed (4)
- **Source Path:** `C:\TEMP\DEPLOYPORTAL\DEPLOY_2_1A16F53723FC4AE3AFD441273C6A423C\PackageAssets`
- **Environment:** Cst-hfx-tst-07 (9f9541eb-43e2-ef11-b8e4-6045bd003904)
- **Deployment Target:** https://c365afspmunified.crm.dynamics.com/
- **Version Info:** 9.2.25123.166

### Эталонный P2 (deploy_CST-HFX-TST-07_20260212_092150)
- **Started:** ~2026-02-12 09:22:00
- **Completed:** ~2026-02-12 09:52:00
- **Duration:** ~30 минут
- **Source Path:** `D:\DOWNLOADS\SCT\1046703\P2\PackageAssets`
- **Environment:** CST-HFX-TST-07 (ef7d39e4-66d2-f011-8729-000d3a33a003)
- **Deployment Target:** https://cst-hfx-tst-07.crm.dynamics.com/
- **Version Info:** 9.2.26014.142

---

## КЛЮЧЕВОЕ РАЗЛИЧИЕ ОБНАРУЖЕНО!

### РАЗНЫЕ ЭНВАЙРОНМЕНТЫ!

**Деплойменты #1 и #2:**
- Organization ID: `9f9541eb-43e2-ef11-b8e4-6045bd003904`
- Organization UniqueName: `unq9f9541eb43e2ef11b8e46045bd003`
- Target URL: `https://c365afspmunified.crm.dynamics.com/`
- PackageType: **Sandbox** 
- PlatformVersion: 7.0.7778.29
- ApplicationVersion: 10.0.2428.63

**Эталонный P2:**
- Organization ID: `ef7d39e4-66d2-f011-8729-000d3a33a003`
- Organization UniqueName: `unqef7d39e466d2f0118729000d3a33a`
- Target URL: `https://cst-hfx-tst-07.crm.dynamics.com/`
- PackageType: **OnlineDev**
- PlatformVersion: 7.0.7690.99
- ApplicationVersion: 10.0.2345.140

---

## Список модулей (одинаковые во всех 3 деплойментах)

Все три деплоймента содержали одинаковый набор из 19 модулей:

1. sisalops_1_0_0_1_managed.zip
2. sisc365alfieldserviceext_1_0_0_1_managed.zip
3. sisconstruct365fsops_1_0_0_1_managed.zip
4. sisconstruct365ops_1_0_0_1_managed.zip
5. sisconstruct365pmops_1_0_0_1_managed.zip
6. sisfieldops_1_0_0_1_managed.zip
7. sisfopsextention_1_0_0_1_managed.zip
8. sisheavyhighway_1_0_0_1_managed.zip
9. sismops_1_0_0_1_managed.zip
10. sispayrollops_1_0_0_1_managed.zip
11. sispayroll_isv_1_0_0_1_managed.zip
12. sispcmbi_1_0_0_1_managed.zip
13. sispcmhcssops_1_0_0_1_managed.zip
14. sispcmheavyhighway_1_0_0_1_managed.zip
15. sispcmperiodclosure_1_0_0_1_managed.zip
16. sispcmprocorealopsintegration_1_0_0_1_managed.zip
17. sispcmprocoreintegration_1_0_0_1_managed.zip
18. sispcmsubcontractmgmt_1_0_0_1_managed.zip
19. sisproject360_1_0_0_1_managed.zip

---

## КРИТИЧНЫЙ ВЫВОД

❌ **ПРОБЛЕМА: При интерактивной аутентификации был выбран неправильный энвайронмент!**

### Причина проблемы:

1. **Environment "Cst-hfx-tst-07" (ID: 6) в базе данных НЕ имеет Service Principal credentials:**
   - TenantId: 77d6f5ce-824c-4d27-a315-4988ab78abb4 ✅
   - ApplicationId: **НЕ ЗАПОЛНЕН** ❌
   - ClientSecretEncrypted: Есть, но без ApplicationId не используется
   - HasServicePrincipal: **FALSE** ❌

2. **Из-за отсутствия Service Principal, деплоймент использовал интерактивную аутентификацию:**
   ```
   pac auth create --environment "cst-hfx-tst-07.crm.dynamics.com" --deviceCode
   ```

3. **При интерактивной аутентификации вы вручную авторизовались через браузер и выбрали другой энвайронмент:**
   - Ожидаемый: `cst-hfx-tst-07.crm.dynamics.com` (CST-HFX-TST-07)
   - Фактически выбранный: `c365afspmunified.crm.dynamics.com` (C365afspmunified)

### Сравнение environments из базы:

| ID | Name | URL | Has ApplicationId | Has ServicePrincipal |
|----|------|-----|-------------------|----------------------|
| 1 | C365afspmunified | c365afspmunified.crm.dynamics.com | ❌ | ✅ (has secret) |
| 6 | Cst-hfx-tst-07 | cst-hfx-tst-07.crm.dynamics.com | ❌ | ❌ |

Оба энвайронмента НЕ имеют ApplicationId, поэтому оба требуют интерактивную аутентификацию!

---

## Различия в версиях платформы

| Параметр | Деплойменты #1 & #2 | Эталонный P2 |
|----------|-------------------|---------------|
| PackageType | Sandbox | OnlineDev |
| PlatformVersion | 7.0.7778.29 | 7.0.7690.99 |
| ApplicationVersion | 10.0.2428.63 | 10.0.2345.140 |
| Org Version | 9.2.25123.166 | 9.2.26014.142 |

---

## Рекомендации

### ✅ НЕМЕДЛЕННЫЕ ДЕЙСТВИЯ:

1. **Добавить ApplicationId для environment "Cst-hfx-tst-07":**
   - Зайдите в портал по адресу http://localhost:5137/environments
   - Отредактируйте environment "Cst-hfx-tst-07"
   - Добавьте ApplicationId (Service Principal Client ID)
   - Убедитесь что ClientSecret уже заполнен

2. **После добавления ApplicationId, environment будет использовать автоматическую аутентификацию:**
   ```bash
   pac auth create --applicationId {AppId} --clientSecret "{Secret}" --tenant {TenantId} --environment {URL}
   ```
   Это исключит человеческий фактор при выборе энвайронмента.

### ✅ ДОПОЛНИТЕЛЬНЫЕ РЕКОМЕНДАЦИИ:

3. **Проверьте все environments на наличие Service Principal:**
   - C365afspmunified (ID: 1) - также НЕ имеет ApplicationId ⚠️
   - Infra-tst-01 (ID: 7) - нужно проверить
   - Другие environments из списка

4. **Для Production environments ОБЯЗАТЕЛЬНО используйте Service Principal:**
   - Это обеспечивает автоматизацию
   - Исключает ошибки при ручном выборе
   - Позволяет использовать API для деплоймента

5. **Документируйте какой ApplicationId соответствует какому environment:**
   - Это поможет избежать путаницы в будущем

---

## КАК ИЗБЕЖАТЬ ЭТОЙ ПРОБЛЕМЫ В БУДУЩЕМ?

1. **Всегда заполняйте Service Principal credentials для environments**
2. **При интерактивной аутентификации ВНИМАТЕЛЬНО проверяйте выбранный environment в браузере**
3. **После деплоймента проверяйте в логах Organization ID и URL чтобы убедиться что попали на правильный энвайронмент**
4. **Используйте API для автоматических деплойментов (требует Service Principal)**

---

## Структура пакетов

Все модули идентичны по размерам и upload времени во всех трех случаях, что подтверждает что проблема НЕ в пакетах, а именно в выборе неправильного энвайронмента при деплое.

Вот финальная временная сводка, которая демонстрирует что пакеты идентичны:
- sisalops: 3.27 сек
- sisc365alfieldserviceext: 1.25 сек
- sispayroll_isv: 5.29 сек (самый большой)
- sispcmbi: 2.13 сек

### Deployment #2 (к неправильному энву)
- sisalops: 2.79 сек
- sisc365alfieldserviceext: 1.22 сек
- sispayroll_isv: 4.68 сек (самый большой)
- sispcmbi: 2.02 сек

### Эталонный P2 (к правильному энву)
- sisalops: 2.98 сек
- sisc365alfieldserviceext: 1.60 сек
- sispayroll_isv: 4.98 сек (самый большой)
- sispcmbi: 1.72 сек

Все времена похожи, что подтверждает что пакеты идентичны по содержимому.
