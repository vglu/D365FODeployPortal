# Secrets audit before making the repository public

This document records what was checked and redacted so the repository can be safely made public.

## What was redacted (2026-02-15)

### Post-mortem / analysis documents

In **PROBLEM_FOUND_SUMMARY.md**, **FINAL_ANALYSIS_WITH_DB_DATA.md**, **ANALYSIS_DEPLOYMENT_ISSUE.md** the following were replaced with placeholders:

| Type | Placeholder | Reason |
|------|-------------|--------|
| Tenant IDs (Azure AD) | `<TENANT_ID>` | Real tenant identifier |
| Application IDs (Service Principal) | `<APPLICATION_ID>` | Real app registration ID |
| Organization IDs (D365 env) | `<ORG_ID_A>`, `<ORG_ID_B>` | Real environment org IDs |
| Organization UniqueNames | `<UNIQUE_NAME_A>`, `<UNIQUE_NAME_B>` | Derived from org IDs |
| Encrypted secret fragment | `<encrypted>` | ClientSecretEncrypted payload (Data Protection) |
| Environment URLs | `<target-env>.crm.dynamics.com`, `<wrong-env>.crm.dynamics.com` | Real D365 environment hostnames |
| Local paths | `<path>\`, `<temp-deploy-path>\` | Machine-specific paths |

### Release notes

- **RELEASE_NOTES_v1.3.1.md** — example Organization Id in script output replaced with `<example-org-id>`.

### Other docs

- **TWO_LEVEL_VALIDATION.md** — example Environment ID replaced with `<example-id>...`.

## What was not changed (intentionally)

- **Support email** (e.g. vhlu@sims-service.com) in release notes — public contact; leave as-is unless you prefer a generic address.
- **CredentialParser.cs** — regex patterns for Russian PAC CLI output (`Срок действия секрета`, etc.) kept for locale compatibility; no secrets.
- **Placeholder examples** in docs (e.g. `your-pat-token`, `{YOUR_APP_ID}`, `"ваш_PAT"`) — already placeholders; safe.
- **GitHub Actions** — use `${{ secrets.GITHUB_TOKEN }}` and `secrets.DOCKERHUB_*`; no values stored in repo.

## Recommended checks before going public

1. **Search for remaining IDs:**  
   `rg -n "[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}" --glob "*.md" .`
2. **Search for connection strings / secrets:**  
   `rg -ni "connectionstring|password\s*=|secret\s*=|api[_-]?key\s*=" --glob "*.{json,cs,env,md}" .`
3. **Ensure no real PAT/tokens:**  
   Any `-Pat "..."` or `AZURE_DEVOPS_PAT` in docs should be placeholders only.
4. **.gitignore:**  
   Already ignores `usersettings.json`, `local.settings.json`, `*.db` — do not remove.

After these steps, the repo can be made public without exposing tenant/app/org IDs or secrets.
