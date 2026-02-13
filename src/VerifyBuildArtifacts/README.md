# VerifyBuildArtifacts

Проверочная консольная утилита: те же вызовы API, что в рабочем Python-скрипте (список артефактов → File Container API 6.0-preview). Вывод в том же формате, что и у Python.

**PAT не хранить в коде.** Задать переменную окружения перед запуском:

**PowerShell:**
```powershell
$env:AZURE_DEVOPS_PAT = "ваш-pat-токен"
dotnet run --project src/VerifyBuildArtifacts
```

**Своя ссылка на билд (опционально):**
```powershell
dotnet run --project src/VerifyBuildArtifacts -- "https://sisn.visualstudio.com/SIS%20D365FO%20Products/_build/results?buildId=107394"
```

По умолчанию используется билд из ссылки на пайплайн (buildId=107394). Результат должен совпадать с выводом Python (Packages: 3 zip, AdditionalLogs: список файлов).
