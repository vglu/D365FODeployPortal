# SOLID Анализ и План рефакторинга DeployService

## 🔍 Анализ текущего состояния

### Нарушения SOLID принципов:

#### 1. **Single Responsibility Principle (SRP)** ⚠️
`DeployService` делает слишком много:
- Управление процессами (PAC CLI)
- Аутентификация
- Валидация (pre-deploy и post-deploy)
- Работа с файловой системой (создание/удаление папок)
- Парсинг логов

**Проблема:** Класс имеет 5+ причин для изменения.

#### 2. **Open/Closed Principle (OCP)** ⚠️
- Валидаторы зашиты внутри метода (сложно добавить новые проверки)
- Парсинг лога жёстко привязан к формату PAC CLI

**Проблема:** Для добавления новой валидации нужно менять `DeployService`.

#### 3. **Liskov Substitution Principle (LSP)** ✅
Не применимо (нет наследования).

#### 4. **Interface Segregation Principle (ISP)** ⚠️
Нет интерфейсов вообще — невозможно мокать для тестов.

**Проблема:** Нельзя протестировать `DeployService` без реального PAC CLI.

#### 5. **Dependency Inversion Principle (DIP)** ⚠️
Зависимости от конкретных классов вместо абстракций:
- `SettingsService` (конкретный класс)
- `SecretProtectionService` (конкретный класс)
- Прямой вызов `Process` (нет абстракции)

**Проблема:** Tight coupling, сложно тестировать.

---

## ✅ План рефакторинга

### 1. Разделить ответственности (SRP)

Создать отдельные компоненты:

```
DeployService (оркестратор)
  ├── IPacAuthService → аутентификация в PAC
  ├── IPacDeploymentService → деплоймент пакетов
  ├── IDeploymentValidator → валидация (коллекция)
  │     ├── PreDeployAuthValidator (pac auth who)
  │     └── PostDeployLogValidator (парсинг лога)
  └── IIsolatedDirectoryManager → управление изолированными папками
```

### 2. Применить Strategy Pattern для валидаторов (OCP)

```csharp
public interface IDeploymentValidator
{
    Task ValidateAsync(DeploymentContext context);
}

public class PreDeployAuthValidator : IDeploymentValidator { }
public class PostDeployLogValidator : IDeploymentValidator { }
```

### 3. Ввести абстракции (ISP + DIP)

```csharp
public interface IPacCliExecutor
{
    Task<string> ExecuteAsync(string command, string workingDir, Dictionary<string, string> envVars);
}

public interface IPacAuthService
{
    Task AuthenticateAsync(Environment env, string isolatedAuthDir);
    Task<string> WhoAmIAsync(string isolatedAuthDir);
}

public interface IPacDeploymentService
{
    Task DeployAsync(string packagePath, string logPath, string isolatedAuthDir);
}
```

### 4. Использовать Composite Pattern для валидаторов

```csharp
public class CompositeDeploymentValidator : IDeploymentValidator
{
    private readonly IEnumerable<IDeploymentValidator> _validators;
    
    public async Task ValidateAsync(DeploymentContext context)
    {
        foreach (var validator in _validators)
            await validator.ValidateAsync(context);
    }
}
```

---

## 📁 Структура файлов после рефакторинга

```
src/DeployPortal/Services/
├── Deployment/
│   ├── IDeployService.cs (интерфейс)
│   ├── DeployService.cs (рефакторенный оркестратор)
│   ├── DeploymentContext.cs (контекст для валидаторов)
│   │
│   ├── PacCli/
│   │   ├── IPacCliExecutor.cs
│   │   ├── PacCliExecutor.cs
│   │   ├── IPacAuthService.cs
│   │   ├── PacAuthService.cs
│   │   ├── IPacDeploymentService.cs
│   │   └── PacDeploymentService.cs
│   │
│   ├── Validation/
│   │   ├── IDeploymentValidator.cs
│   │   ├── CompositeDeploymentValidator.cs
│   │   ├── PreDeployAuthValidator.cs
│   │   └── PostDeployLogValidator.cs
│   │
│   └── Isolation/
│       ├── IIsolatedDirectoryManager.cs
│       └── IsolatedDirectoryManager.cs
```

---

## 🧪 Тестовое покрытие после рефакторинга

### Unit Tests:
- [x] `PacCliExecutorTests` — мок процесса
- [x] `PacAuthServiceTests` — тесты аутентификации
- [x] `PacDeploymentServiceTests` — тесты деплоймента
- [x] `PreDeployAuthValidatorTests` — валидация pac auth who
- [x] `PostDeployLogValidatorTests` — парсинг лога
- [x] `IsolatedDirectoryManagerTests` — создание/удаление папок
- [x] `DeployServiceTests` — оркестрация (мокаем все зависимости)

### Integration Tests:
- [x] `DeployServiceIntegrationTests` — с реальными файлами, но мок PAC CLI

### E2E Tests:
- [x] `DeploymentE2ETests` — полный сценарий с Docker Test Containers

---

## 📊 Метрики улучшения

| Метрика | До рефакторинга | После рефакторинга |
|---------|-----------------|---------------------|
| Классов | 1 | 10 |
| Строк в DeployService | 290 | ~80-100 |
| Тестируемость | ❌ Нельзя мокать | ✅ Все интерфейсы |
| Cyclomatic Complexity | ~15 | ~5 |
| Причин для изменения (SRP) | 5+ | 1 |
| Test Coverage | 0% | 90%+ |

---

## 🚀 Порядок выполнения

1. ✅ Создать интерфейсы и базовые классы
2. ✅ Рефакторить DeployService (разбить на компоненты)
3. ✅ Написать юнит-тесты для каждого компонента
4. ✅ Написать интеграционные тесты
5. ✅ Написать E2E тесты
6. ✅ Обновить DI регистрации в Program.cs
7. ✅ Проверить что всё работает (запустить реальный деплоймент)

---

**Статус:** Готов к началу рефакторинга  
**Оценка времени:** ~200-300 tool calls
