# Azure App Configuration + Feature Flags with ASP.NET Core (.NET 10)

A small, focused sample that shows how an **ASP.NET Core Web API** integrates with
**Azure App Configuration** and **Azure Feature Flags** — with a deliberate
emphasis on **authentication and identity**. It demonstrates *every* way a Web API
can authenticate to App Configuration, from access-key connection strings all the
way to **Managed Identity** (the recommended production approach).

Infrastructure is provisioned with **Terraform**. Everything is kept intentionally
simple so the integration points stay front and centre.

---

## What's inside

```
AzureAppConfig/
├── src/AppConfigDemo/            # .NET 10 ASP.NET Core Web API (minimal API)
│   ├── Program.cs                # App Config + Feature Flags wiring
│   └── AppConfigAuth/
│       ├── AppConfigOptions.cs           # settings + the AuthMethod enum
│       └── AppConfigCredentialFactory.cs # every auth method, one per branch
├── terraform/                    # Azure resources (App Config, identity, RBAC, flags)
│   ├── main.tf                   # store, user-assigned identity, role assignments, seed data
│   ├── app_service.tf            # OPTIONAL App Service w/ system-assigned managed identity
│   ├── providers.tf variables.tf outputs.tf
│   └── terraform.tfvars.example
├── docs/authentication.md        # deep dive on all auth methods (read this!)
└── README.md
```

## The authentication methods (the point of this repo)

The app supports **8** ways to connect, switchable with a single setting
(`AppConfig:AuthMethod`). They split into two styles:

- **Access key** — `options.Connect(connectionString)` (a secret).
- **Microsoft Entra ID (RBAC)** — `options.Connect(endpoint, tokenCredential)`
  (the recommended path; Managed Identity needs no secret at all).

| `AuthMethod` | Style | Secret? | Best for |
|---|---|---|---|
| `ConnectionString` | Access key | Yes | Quick spikes / demos |
| `DefaultAzureCredential` | Entra ID | No* | **Default** — local *and* cloud |
| `SystemAssignedManagedIdentity` | Entra ID | **No** | **Production in Azure** ✅ |
| `UserAssignedManagedIdentity` | Entra ID | **No** | Shared identity across resources |
| `ServicePrincipalSecret` | Entra ID | Yes | Non-Azure hosts / CI |
| `ServicePrincipalCertificate` | Entra ID | Yes (cert) | Non-Azure, stronger than secret |
| `AzureCli` | Entra ID | No | Local development |
| `WorkloadIdentity` | Entra ID | **No** | AKS / Kubernetes |

\* uses your `az login` locally and Managed Identity in Azure — no secret in either.

👉 **Full explanation of each method, with code and trade-offs, is in
[`docs/authentication.md`](docs/authentication.md).** The implementation is one
readable `switch` in
[`AppConfigCredentialFactory.cs`](src/AppConfigDemo/AppConfigAuth/AppConfigCredentialFactory.cs).

All Entra ID methods require the **`App Configuration Data Reader`** RBAC role on
the store. Terraform sets this up for you.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure subscription + [Azure CLI](https://learn.microsoft.com/cli/azure/) (`az login`)
- [Terraform](https://developer.hashicorp.com/terraform/downloads) ≥ 1.6
  *(or run the Terraform from [Azure Cloud Shell](https://shell.azure.com), which
  has it pre-installed)*

---

## Quick start

### 0. Run the app with no Azure at all (optional)

The app boots in **local-only mode** if no endpoint is configured, so you can
explore the code immediately:

```powershell
cd src/AppConfigDemo
dotnet run
# then browse the URL it prints, e.g. http://localhost:5212/auth/info
```

`/config` and `/features` will be empty until you connect a store.

### 1. Provision Azure with Terraform

```powershell
cd terraform
Copy-Item terraform.tfvars.example terraform.tfvars   # edit if you like
az login                                              # Terraform reuses this
terraform init
terraform apply
```

This creates a resource group, an App Configuration store, a user-assigned
managed identity, the RBAC role assignments, and seeds sample config + feature
flags. Grab the outputs:

```powershell
terraform output app_configuration_endpoint
terraform output user_assigned_identity_client_id
terraform output -raw app_configuration_connection_string   # sensitive
```

### 2. Point the app at the store

Easiest path — **`DefaultAzureCredential`** (uses your `az login`):

```powershell
cd ../src/AppConfigDemo
dotnet user-secrets init
dotnet user-secrets set "AppConfig:AuthMethod" "DefaultAzureCredential"
dotnet user-secrets set "AppConfig:Endpoint" "<app_configuration_endpoint>"
dotnet run
```

> User-secrets keep values out of source control. You can equally set
> `appsettings.json`, or env vars like `AppConfig__Endpoint`.

### 3. Try the endpoints

| Endpoint | What it shows |
|---|---|
| `GET /` | Overview + links |
| `GET /auth/info` | **Which auth method/style is in use, required RBAC role** |
| `GET /config` | Config values loaded from the store |
| `GET /features` | Every feature flag and whether it's enabled |
| `GET /api/beta` | `200` when `BetaFeature` is on, else `404` |
| `GET /health` | Health check |

```powershell
curl http://localhost:5212/auth/info
curl http://localhost:5212/features
```

---

## Switching auth methods

Change one setting and restart. Examples:

```powershell
# Access-key connection string
dotnet user-secrets set "AppConfig:AuthMethod" "ConnectionString"
dotnet user-secrets set "AppConfig:ConnectionString" "<connection-string>"

# User-assigned managed identity (when running on Azure compute with it attached)
dotnet user-secrets set "AppConfig:AuthMethod" "UserAssignedManagedIdentity"
dotnet user-secrets set "AppConfig:ManagedIdentityClientId" "<client-id>"

# Service principal with a secret
dotnet user-secrets set "AppConfig:AuthMethod" "ServicePrincipalSecret"
dotnet user-secrets set "AppConfig:TenantId" "<tenant>"
dotnet user-secrets set "AppConfig:ClientId" "<client-id>"
dotnet user-secrets set "AppConfig:ClientSecret" "<secret>"
```

See [`docs/authentication.md`](docs/authentication.md) for all eight.

## Demonstrating Managed Identity end-to-end

Managed Identity only works **inside Azure**. To see it for real, deploy the
optional App Service (it comes pre-wired with a system-assigned identity and the
*Data Reader* role):

```powershell
cd terraform
terraform apply -var="deploy_app_service=true"

# publish + deploy the code
cd ../src/AppConfigDemo
dotnet publish -c Release -o publish
Compress-Archive -Path publish/* -DestinationPath app.zip -Force
az webapp deploy -g rg-appconfig-demo -n <app-name-from-output> --src-path app.zip --type zip
```

The web app is configured with `AppConfig__AuthMethod = SystemAssignedManagedIdentity`,
so `GET https://<app>.azurewebsites.net/auth/info` will report it authenticating
with **no secrets**.

---

## How the integration works

- **Loading config** — `AddAzureAppConfiguration(...)` registers the store as a
  configuration source. The app selects keys prefixed `AppConfigDemo:` and trims
  the prefix, so `AppConfigDemo:Message` is read as `config["Message"]`.
- **Feature flags** — `options.UseFeatureFlags(...)` + `AddFeatureManagement()`
  expose flags via `IFeatureManager`. `/api/beta` is gated on `BetaFeature`.
- **Dynamic refresh** — the app watches a **sentinel** key. Bump `Sentinel` in
  Terraform/portal and changes flow to running apps within the refresh interval
  (default 30s) with no restart. `app.UseAzureAppConfiguration()` enables it.

## Cleanup

```powershell
cd terraform
terraform destroy
```

## Notes & cost

- Default SKU is `standard`. Use `sku = "free"` for the cheapest option (only one
  free store per subscription). The optional App Service uses a `B1` plan, which
  is **not** free — destroy it when done.
- The connection string in Terraform outputs is sensitive; it's git-ignored via
  state files but never commit `terraform.tfstate`.
- Built and smoke-tested on .NET 10 (`Microsoft.Azure.AppConfiguration.AspNetCore`
  8.5, `Microsoft.FeatureManagement.AspNetCore` 4.5, `Azure.Identity` 1.21).
