# Authenticating an ASP.NET Core Web API to Azure App Configuration

This is the deep-dive companion to the README. It explains **every** way the
sample can connect to Azure App Configuration, how each one works, when to use
it, and how to switch the sample to it.

> The single switch is `AppConfig:AuthMethod` in `appsettings.json` (or the
> `AppConfig__AuthMethod` environment variable). All methods are implemented in
> [`AppConfigCredentialFactory.cs`](../src/AppConfigDemo/AppConfigAuth/AppConfigCredentialFactory.cs)
> and selected in [`Program.cs`](../src/AppConfigDemo/Program.cs).

---

## The two connection styles

Everything reduces to one of two styles:

| # | Style | Code shape | Secret? |
|---|-------|-----------|---------|
| **A** | **Access key** | `options.Connect(connectionString)` | Yes — the key is the secret |
| **B** | **Microsoft Entra ID (RBAC)** | `options.Connect(endpointUri, tokenCredential)` | No (Managed Identity) or yes (SP secret) |

Style **B** is the recommended path. The only thing that changes between its
many flavours is *which `TokenCredential`* you hand to `Connect`. Azure ships
those credential classes in the **`Azure.Identity`** package.

For style B, the identity must hold the **`App Configuration Data Reader`** RBAC
role on the store (data plane). Writing needs **`App Configuration Data Owner`**.
Control-plane roles like *Contributor* do **not** grant data access.

---

## Method-by-method

### 1. `ConnectionString` — access key (style A)

```jsonc
"AuthMethod": "ConnectionString",
"ConnectionString": "Endpoint=https://...;Id=...;Secret=..."
```

- **How:** the connection string embeds an HMAC access key. No Entra ID involved.
- **Pros:** dead simple, works anywhere, no RBAC setup.
- **Cons:** it's a long-lived shared secret. If it leaks, anyone can read/write.
  You have to store and rotate it. Disable with `local_auth_enabled = false`
  on the store to force Entra ID only.
- **Use when:** quick spikes, demos, or environments where you genuinely can't
  use Entra ID. Keep the string in user-secrets / Key Vault / env vars, never in
  source.

### 2. `DefaultAzureCredential` — the recommended default (style B)

```jsonc
"AuthMethod": "DefaultAzureCredential",
"Endpoint": "https://<store>.azconfig.io"
```

- **How:** tries an ordered chain and uses the first credential that works:
  environment variables → Workload Identity → Managed Identity → Azure CLI →
  Azure Developer CLI → Visual Studio / VS Code.
- **Pros:** the **same code runs locally and in Azure**. On your machine it uses
  your `az login`; deployed to Azure it uses the resource's managed identity. No
  `#if` or per-environment branching.
- **Cons:** the chain is implicit; when it picks the "wrong" identity the error
  can be confusing. Pin the managed identity with `ManagedIdentityClientId` when
  a resource has more than one.
- **Use when:** almost always. This is the default in the sample.

### 3. `SystemAssignedManagedIdentity` (style B)

```jsonc
"AuthMethod": "SystemAssignedManagedIdentity",
"Endpoint": "https://<store>.azconfig.io"
```

- **How:** `new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)`. The
  Azure platform issues tokens for the hosting resource's own identity. The
  identity is created/destroyed with the resource.
- **Pros:** **no secrets at all.** Best-practice for production in Azure.
- **Cons:** only works *inside* Azure (App Service, Container Apps, Functions,
  VM, AKS…). Can't be used from a laptop.
- **Try it:** set `deploy_app_service = true` in Terraform — it provisions an App
  Service with a system-assigned identity already granted *Data Reader* and
  configured to use this method.

### 4. `UserAssignedManagedIdentity` (style B)

```jsonc
"AuthMethod": "UserAssignedManagedIdentity",
"Endpoint": "https://<store>.azconfig.io",
"ManagedIdentityClientId": "<client-id-from-terraform-output>"
```

- **How:** `new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(...))`.
  A standalone identity you create once and attach to many resources.
- **Pros:** still secret-less; survives resource recreation; one identity can be
  shared across many apps and pre-granted roles.
- **Cons:** you manage its lifecycle and must pass the right client id.
- **Try it:** Terraform always creates one — grab `user_assigned_identity_client_id`
  from the outputs. (You still need to *attach* it to an Azure compute resource
  to actually authenticate.)

### 5. `ServicePrincipalSecret` (style B)

```jsonc
"AuthMethod": "ServicePrincipalSecret",
"Endpoint": "https://<store>.azconfig.io",
"TenantId": "...", "ClientId": "...", "ClientSecret": "..."
```

- **How:** `new ClientSecretCredential(tenantId, clientId, clientSecret)` against
  an Entra app registration.
- **Pros:** works anywhere (other clouds, on-prem, CI/CD).
- **Cons:** you own a secret again (rotation, leakage). Prefer Managed Identity
  in Azure and a certificate or federated credential elsewhere.
- **Use when:** non-Azure hosts or pipelines without Workload Identity.

### 6. `ServicePrincipalCertificate` (style B)

```jsonc
"AuthMethod": "ServicePrincipalCertificate",
"Endpoint": "https://<store>.azconfig.io",
"TenantId": "...", "ClientId": "...", "ClientCertificatePath": "C:\\path\\to\\cert.pfx"
```

- **How:** `new ClientCertificateCredential(tenantId, clientId, certPath)`.
- **Pros:** stronger than a shared secret — the private key never leaves the host.
- **Cons:** certificate lifecycle/rotation to manage.
- **Use when:** non-Azure host that needs better-than-secret security.

### 7. `AzureCli` — local development (style B)

```jsonc
"AuthMethod": "AzureCli",
"Endpoint": "https://<store>.azconfig.io"
```

- **How:** `new AzureCliCredential()` reuses whoever ran `az login`.
- **Pros:** zero secret config for local dev.
- **Cons:** **dev only** — there's no `az` on a server. (`DefaultAzureCredential`
  already includes this in its chain, so you rarely need it explicitly.)

### 8. `WorkloadIdentity` — AKS / Kubernetes (style B)

```jsonc
"AuthMethod": "WorkloadIdentity",
"Endpoint": "https://<store>.azconfig.io"
```

- **How:** `new WorkloadIdentityCredential()`. A federated credential exchanges a
  projected Kubernetes service-account token for an Entra token. AKS injects the
  required env vars (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_FEDERATED_TOKEN_FILE`) when the pod is labelled for workload identity.
- **Pros:** secret-less identity for workloads running in AKS.
- **Cons:** AKS-specific setup (OIDC issuer, federated credential, pod labels).
- **Use when:** the API runs in AKS. (`DefaultAzureCredential` also covers this.)

---

## Which should I use?

```
Running in Azure (App Service / Container Apps / Functions / VM)?
  └─ Yes → System- or User-assigned Managed Identity   (no secrets) ✅
Running in AKS?
  └─ Yes → Workload Identity                            (no secrets) ✅
Running elsewhere (other cloud / on-prem / CI)?
  └─ Yes → Service Principal (certificate > secret)
Local development?
  └─ Azure CLI (via DefaultAzureCredential)

When in doubt → DefaultAzureCredential. It picks the right one per environment.
```

## RBAC cheat-sheet

| Action | Role |
|--------|------|
| Read config + feature flags (the app) | **App Configuration Data Reader** |
| Write keys / flags (Terraform, admins) | **App Configuration Data Owner** |
| Manage the store resource itself | Contributor / Owner (control plane) |

Grant from the CLI if needed:

```bash
az role assignment create \
  --assignee <principal-or-client-id> \
  --role "App Configuration Data Reader" \
  --scope <app-configuration-resource-id>
```

> RBAC changes are eventually consistent — allow ~1–2 minutes before tokens
> reflect a new assignment.
