# SOLID Analysis and DeployService Refactoring Plan

## Current State Analysis

### SOLID Violations:

#### 1. **Single Responsibility Principle (SRP)** ⚠️
`DeployService` does too much:
- Process management (PAC CLI)
- Authentication
- Validation (pre-deploy and post-deploy)
- File system (create/delete folders)
- Log parsing

**Problem:** The class has 5+ reasons to change.

#### 2. **Open/Closed Principle (OCP)** ⚠️
- Validators are hardcoded inside the method (hard to add new checks)
- Log parsing is tightly coupled to PAC CLI output format

**Problem:** Adding a new validator requires changing `DeployService`.

#### 3. **Liskov Substitution Principle (LSP)** ✅
Not applicable (no inheritance).

#### 4. **Interface Segregation Principle (ISP)** ⚠️
No interfaces — cannot mock for tests.

**Problem:** Cannot test `DeployService` without real PAC CLI.

#### 5. **Dependency Inversion Principle (DIP)** ⚠️
Dependencies on concrete classes instead of abstractions:
- `SettingsService` (concrete)
- `SecretProtectionService` (concrete)
- Direct `Process` usage (no abstraction)

**Problem:** Tight coupling, hard to test.

---

## Refactoring Plan

### 1. Split responsibilities (SRP)

Introduce separate components:

```
DeployService (orchestrator)
  ├── IPacAuthService → PAC authentication
  ├── IPacDeploymentService → package deployment
  ├── IDeploymentValidator → validation (collection)
  │     ├── PreDeployAuthValidator (pac auth who)
  │     └── PostDeployLogValidator (log parsing)
  └── IIsolatedDirectoryManager → isolated folder management
```

### 2. Apply Strategy Pattern for validators (OCP)

```csharp
public interface IDeploymentValidator
{
    Task ValidateAsync(DeploymentContext context);
}

public class PreDeployAuthValidator : IDeploymentValidator { }
public class PostDeployLogValidator : IDeploymentValidator { }
```

### 3. Introduce abstractions (ISP + DIP)

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

### 4. Use Composite Pattern for validators

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

## File Structure After Refactoring

```
src/DeployPortal/Services/
├── Deployment/
│   ├── IDeployService.cs (interface)
│   ├── DeployService.cs (refactored orchestrator)
│   ├── DeploymentContext.cs (context for validators)
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

## Test Coverage After Refactoring

### Unit Tests:
- [x] `PacCliExecutorTests` — process mock
- [x] `PacAuthServiceTests` — authentication tests
- [x] `PacDeploymentServiceTests` — deployment tests
- [x] `PreDeployAuthValidatorTests` — pac auth who validation
- [x] `PostDeployLogValidatorTests` — log parsing
- [x] `IsolatedDirectoryManagerTests` — create/delete folders
- [x] `DeployServiceTests` — orchestration (all dependencies mocked)

### Integration Tests:
- [x] `DeployServiceIntegrationTests` — real files, mock PAC CLI

### E2E Tests:
- [x] `DeploymentE2ETests` — full scenario with Docker Test Containers

---

## Improvement Metrics

| Metric | Before | After |
|--------|--------|-------|
| Classes | 1 | 10 |
| Lines in DeployService | 290 | ~80-100 |
| Testability | ❌ Cannot mock | ✅ All interfaces |
| Cyclomatic Complexity | ~15 | ~5 |
| Reasons to change (SRP) | 5+ | 1 |
| Test Coverage | 0% | 90%+ |

---

## Execution Order

1. ✅ Create interfaces and base classes
2. ✅ Refactor DeployService (split into components)
3. ✅ Add unit tests for each component
4. ✅ Add integration tests
5. ✅ Add E2E tests
6. ✅ Update DI registration in Program.cs
7. ✅ Verify end-to-end (run a real deployment)

---

**Status:** Ready to start refactoring  
**Estimated effort:** ~200-300 tool calls
