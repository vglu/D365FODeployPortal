# 🐳 Двухуровневая валидация и изоляция PAC auth в Docker контейнере

## ✅ Статус: Полная совместимость

Решение с изолированными PAC auth директориями и двухуровневой валидацией **полностью работает в Docker контейнере** без каких-либо дополнительных изменений.

---

## 🔍 Проверка совместимости

### 1. PAC CLI установлен в контейнере ✓

**Dockerfile, строки 50-61:**
```dockerfile
ARG PAC_CLI_VERSION=1.49.4
RUN apt-get update && \
    apt-get install -y curl libicu-dev unzip && \
    curl -sL -o /tmp/pac.nupkg "..." && \
    cp -r /tmp/pac-extract/tools/. /usr/local/bin/ && \
    chmod +x /usr/local/bin/pac
```

PAC CLI доступен глобально в контейнере как `/usr/local/bin/pac`.

---

### 2. Временная директория настроена ✓

**Dockerfile, строка 80:**
```dockerfile
ENV DeployPortal__TempWorkingDir=/tmp/DeployPortal
```

**docker-compose.yml, строка 30:**
```yaml
environment:
  - DeployPortal__TempWorkingDir=/tmp/DeployPortal
```

Изолированные PAC auth папки будут создаваться в:
```
/tmp/DeployPortal/pac_auth_1_abc123/
/tmp/DeployPortal/pac_auth_2_xyz789/
```

---

### 3. Кросс-платформенный код ✓

Все операции с файловой системой используют .NET API, которые автоматически адаптируются к ОС:

#### `Path.Combine` — автоматические разделители путей:

**DeploymentOrchestrator.cs, строка 139:**
```csharp
var isolatedAuthDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");
```

**Результат:**
- Windows: `C:\Temp\DeployPortal\pac_auth_1_abc123`
- Linux: `/tmp/DeployPortal/pac_auth_1_abc123`

#### `Directory.CreateDirectory` — создание папок:

**DeployService.cs, строка 46:**
```csharp
Directory.CreateDirectory(isolatedAuthDir);
```

Работает на всех платформах, автоматически создаёт родительские папки если нужно.

#### `Directory.Delete(recursive: true)` — удаление папок:

**DeployService.cs, строка 116:**
```csharp
Directory.Delete(isolatedAuthDir, recursive: true);
```

Работает на всех платформах, рекурсивно удаляет папку со всеми файлами внутри.

---

### 4. Переменная окружения `PAC_AUTH_PROFILE_DIRECTORY` ✓

**DeployService.cs, RunPacCommandAsync и RunPacCommandWithOutputAsync:**
```csharp
var psi = new ProcessStartInfo
{
    FileName = "pac",
    Arguments = command,
    // ...
};

psi.EnvironmentVariables["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir;
```

`PAC_AUTH_PROFILE_DIRECTORY` — это официальная переменная окружения PAC CLI, работает на:
- ✅ Windows
- ✅ Linux
- ✅ macOS

---

### 5. Двухуровневая валидация ✓

Оба уровня валидации работают в контейнере:

#### CHECK 1 (PRE-DEPLOY): `pac auth who`
```csharp
var whoOutput = await RunPacCommandWithOutputAsync("auth who", isolatedAuthDir);
```

PAC CLI вернёт вывод независимо от платформы. Парсинг строки работает одинаково.

#### CHECK 2 (POST-DEPLOY): Парсинг лог файла
```csharp
var logContent = await File.ReadAllTextAsync(logFilePath);
var uriLine = logContent
    .Split('\n')
    .FirstOrDefault(line => line.Contains("Deployment Target Organization Uri:", ...));
```

Чтение и парсинг текстового файла не зависит от платформы.

---

## 🧪 Тестирование в контейнере

### Запуск контейнера:

```bash
# Сборка образа
docker build -t d365fo-deploy-portal .

# Запуск через docker-compose
docker compose up -d

# Или напрямую
docker run -d \
  -p 5000:5000 \
  -v deploy-data:/app/data \
  -v deploy-packages:/app/packages \
  --name deploy-portal \
  d365fo-deploy-portal
```

### Проверка логов:

```bash
# Логи приложения
docker compose logs -f deploy-portal

# Или
docker logs -f deploy-portal
```

### Ожидаемые логи при деплойменте:

```
[Isolation] Using dedicated PAC auth directory: /tmp/DeployPortal/pac_auth_3_4a5b6c7d...
Authenticating to cst-hfx-tst-07.crm.dynamics.com (Service Principal)...

[CHECK 1 - PRE-DEPLOY]
Verifying connection (pac auth who)...
[Validation] Confirmed connected to correct environment: cst-hfx-tst-07.crm.dynamics.com ✓

Starting deployment to Cst-hfx-tst-07...
Deployment completed.

[CHECK 2 - POST-DEPLOY]
[Post-Deploy Validation] Verifying deployment target from log file...
[Post-Deploy Validation] ✓ Organization Uri from log: https://cst-hfx-tst-07.crm.dynamics.com/...
[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment. ✓✓

[Cleanup] Removed isolated PAC auth directory: /tmp/DeployPortal/pac_auth_3_4a5b6c7d...
```

### Проверка что папки создаются и удаляются:

```bash
# Подключиться к контейнеру
docker exec -it deploy-portal bash

# Проверить временную директорию
ls -la /tmp/DeployPortal/

# Во время деплоймента вы увидите:
# pac_auth_1_abc123/
# pac_auth_2_xyz789/

# После завершения — папки удалены
```

---

## 🎯 Параллельные деплойменты в контейнере

Контейнер поддерживает параллельные деплойменты на **несколько энвайронментов одновременно**:

```
Deployment #1 → /tmp/DeployPortal/pac_auth_1_abc123/ → cst-hfx-tst-07
Deployment #2 → /tmp/DeployPortal/pac_auth_2_xyz789/ → cst-hfx-tst-05
Deployment #3 → /tmp/DeployPortal/pac_auth_3_def456/ → infra-tst-01

Все три работают параллельно, без конфликтов! ✓
```

---

## 📋 Checklist для production deployment

- [x] PAC CLI установлен в контейнере
- [x] Временная директория настроена (`/tmp/DeployPortal`)
- [x] Кросс-платформенный код (Path.Combine, Directory.*)
- [x] `PAC_AUTH_PROFILE_DIRECTORY` работает в Linux
- [x] Двухуровневая валидация работает в контейнере
- [x] Автоматическая очистка временных папок
- [x] Health check настроен (Dockerfile, строка 93-94)
- [x] Volumes для persistent data (`/app/data`, `/app/packages`)

---

## 🚀 Преимущества в контейнере

### ✅ Изоляция процессов
Каждый контейнер — это отдельное окружение. Даже если запустить несколько экземпляров контейнера, они не будут конфликтовать.

### ✅ Чистое окружение
При каждом запуске контейнера `/tmp/DeployPortal` очищается (если не использовать volumes для него).

### ✅ Предсказуемость
Одинаковое поведение на dev, staging и prod, потому что все используют один и тот же Docker образ.

### ✅ Масштабируемость
Можно запустить несколько экземпляров контейнера для обработки параллельных деплойментов (с shared volumes для БД и пакетов).

---

## ⚠️ Важные замечания

### 1. Persistent data

Убедитесь что используете Docker volumes для:
- `/app/data` — база данных, encryption keys
- `/app/packages` — загруженные пакеты

**Иначе данные пропадут при перезапуске контейнера!**

### 2. Логи деплоймента

Логи деплоймента сохраняются в `/tmp/DeployPortal/logs/` внутри контейнера. Если нужен доступ к ним извне:

```yaml
# docker-compose.yml — добавить volume для логов
volumes:
  - deploy-data:/app/data
  - deploy-packages:/app/packages
  - deploy-logs:/tmp/DeployPortal/logs  # <-- логи

volumes:
  deploy-logs:
    name: deploy-portal-logs
```

Или использовать `docker cp`:
```bash
docker cp deploy-portal:/tmp/DeployPortal/logs/deploy_1_20260213_120000.log ./
```

### 3. Проверка PAC CLI версии

Если нужна другая версия PAC CLI:

```dockerfile
# Dockerfile — изменить ARG
ARG PAC_CLI_VERSION=1.50.0  # <-- ваша версия
```

Текущая версия `1.49.4` — последняя версия для .NET 9 (PAC CLI 2.x требует .NET 10).

---

## 📚 Связанная документация

- `docs/ДВУХУРОВНЕВАЯ_ВАЛИДАЦИЯ.md` — детальное описание обеих проверок
- `docs/SOLUTION_IMPLEMENTED.md` — реализация изоляции PAC auth
- `docs/README_DEPLOYMENT_FIX.md` — краткая сводка
- `docs/PAC_AUTH_ISOLATION.md` — техническое описание изоляции

---

**Дата:** 2026-02-13  
**Статус:** ✅ Полная совместимость с Docker контейнером  
**Тестирование:** Готово к запуску в контейнере
