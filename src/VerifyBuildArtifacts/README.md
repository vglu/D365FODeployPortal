# VerifyBuildArtifacts

Console verification utility: same API calls as the production Python script (list artifacts → File Container API 6.0-preview). Output format matches the Python script.

**Do not store PAT in code.** Set the environment variable before running:

**PowerShell:**
```powershell
$env:AZURE_DEVOPS_PAT = "your-pat-token"
dotnet run --project src/VerifyBuildArtifacts
```

**Custom build link (optional):**
```powershell
dotnet run --project src/VerifyBuildArtifacts -- "https://sisn.visualstudio.com/SIS%20D365FO%20Products/_build/results?buildId=107394"
```

By default the pipeline build from the link above (buildId=107394) is used. The result should match the Python output (Packages: 3 zip, AdditionalLogs: list of files).
