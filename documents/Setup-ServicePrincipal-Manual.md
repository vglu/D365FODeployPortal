# Instructions for Azure AD Administrator

## Request: Create a Service Principal for automated D365FO package deployment via Power Platform CLI (PAC)

**Purpose:** Enable non-interactive (browserless) authentication for the PAC CLI tool to automatically deploy Dynamics 365 Finance & Operations packages to the specified Power Platform environments.

> **Automation:** Instead of performing these steps manually, you can run the `scripts/Setup-ServicePrincipal.ps1` script from the project root. It will execute all steps automatically.

---

## Script Automation Options

```powershell
# 1. Full setup — creates SP and registers in all default environments
.\scripts\Setup-ServicePrincipal.ps1

# 2. Full setup with a custom environment list
.\scripts\Setup-ServicePrincipal.ps1 -Environments "env1.crm.dynamics.com","env2.crm.dynamics.com"

# 3. Add a SINGLE new environment to an existing SP
.\scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com"

# 4. Add MULTIPLE new environments to an existing SP
.\scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "env-a.crm.dynamics.com","env-b.crm.dynamics.com"

# 5. Add an environment to an SP with a custom display name
.\scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "new-env.crm.dynamics.com" -AppDisplayName "My-Custom-SP"
```

When using `-AddEnvironment`, the script does **not** create a new Client Secret and does **not** modify API permissions — it only registers the Application User in the specified environments.

---

## If the administrator prefers to set up manually

Step-by-step instructions below.

---

## Part 1: Register Application in Microsoft Entra ID (Azure AD)

1. Go to [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App registrations**
2. Click **New registration**
3. Fill in:
   - **Name:** `PAC-Deploy-Automation` (or another descriptive name)
   - **Supported account types:** `Accounts in this organizational directory only (Single tenant)`
   - **Redirect URI:** leave empty (not needed)
4. Click **Register**
5. On the created application page, note down:
   - **Application (client) ID** — needed later
   - **Directory (tenant) ID** — needed later

---

## Part 2: Create Client Secret

1. On the `PAC-Deploy-Automation` application page, go to **Certificates & secrets**
2. **Client secrets** tab → click **New client secret**
3. Fill in:
   - **Description:** `PAC CLI deploy key`
   - **Expires:** choose a duration (12 or 24 months recommended)
4. Click **Add**
5. **IMPORTANT:** Immediately copy the value from the **Value** column — it is only shown once. This is the **Client Secret**.

---

## Part 3: Assign API Permissions

1. On the application page, go to **API permissions**
2. Click **Add a permission**
3. Select the **APIs my organization uses** tab
4. Search for and select **Dataverse** (may appear as `Common Data Service`)
5. Select **Delegated permissions** → check **user_impersonation**
6. Click **Add permissions**
7. Click **Grant admin consent for [Your tenant]** — and confirm

The result should look like this:

| API / Permission name          | Type      | Status                    |
|--------------------------------|-----------|---------------------------|
| Dataverse / user_impersonation | Delegated | Granted for [tenant name] |

---

## Part 4: Create Application User in Each Power Platform Environment

This must be done **for each environment** where you plan to deploy packages.

### Via Power Platform Admin Center (recommended):

1. Go to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com)
2. Select **Environments** → choose the target environment
3. Click **Settings** (at the top)
4. Section **Users + permissions** → **Application users**
5. Click **+ New app user**
6. Click **+ Add an app** → find `PAC-Deploy-Automation` by name or Application ID → select it
7. Choose **Business Unit** (usually the root one)
8. In **Security roles** add: **System Administrator**
9. Click **Create**

### Repeat for each of your target environments:

| #  | Environment URL                       |
|----|---------------------------------------|
| 1  | `your-env-01.crm.dynamics.com`       |
| 2  | `your-env-02.crm.dynamics.com`       |
| .. | *(add all your target environments)*  |

*(replace with your actual Power Platform environment URLs)*

---

## Part 5: What to Send Back

After completing all steps, provide the following information to the requester:

```
Application (Client) ID:  xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Directory (Tenant) ID:    xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Client Secret (Value):    xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Secret expires:           YYYY-MM-DD
```

> **Warning:** The Client Secret is essentially a password. It should be transmitted securely (not via plain email). Use a corporate password manager, Azure Key Vault, or at least an encrypted message.

---

## Verification (performed by the requester)

After receiving the credentials, verify they work using:

```powershell
pac auth create `
  --applicationId "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
  --clientSecret "xxxxxxxxxxxxxxxxxxxxxxxxxx" `
  --tenant "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
  --environment "your-env-01.crm.dynamics.com"

pac auth who
```

Expected result — successful authentication without opening a browser, `pac auth who` shows the environment name and authentication type `ServicePrincipal`.

---

## Adding a New Environment (if SP already exists)

If the Service Principal was created earlier and you need to add a new environment — only **Part 4** needs to be performed for the new environment.

### Automatically (script):

```powershell
.\scripts\Setup-ServicePrincipal.ps1 -AddEnvironment "new-environment.crm.dynamics.com"
```

### Manually (portal):

1. Go to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com)
2. Select **Environments** → choose the new environment
3. **Settings** → **Users + permissions** → **Application users**
4. **+ New app user** → find `PAC-Deploy-Automation` → select it
5. **Business Unit:** root → **Security roles:** System Administrator → **Create**

Parts 1-3 and 5 do **not** need to be repeated — they are performed only once.

---

## Security Notes

- This Service Principal will receive **full administrative rights** in the specified environments — this is required for package deployment
- It is recommended to create a dedicated security group and Conditional Access Policy if needed
- Monitor Client Secret expiration and renew it before it expires
- In the long term, it is recommended to store the Client Secret in Azure Key Vault
