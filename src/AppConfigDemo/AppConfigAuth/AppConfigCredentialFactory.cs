using Azure.Core;
using Azure.Identity;

namespace AppConfigDemo.AppConfigAuth;

/// <summary>
/// Central place that maps a configured authentication method to an Azure
/// <see cref="TokenCredential"/> (for Microsoft Entra ID / RBAC based access).
///
/// Azure App Configuration can be reached in two fundamentally different ways:
///
///   1. CONNECTION STRING  -> uses an access key embedded in a secret string.
///                            No <see cref="TokenCredential"/> is involved.
///                            See <see cref="AppConfigAuthMethod.ConnectionString"/>.
///
///   2. MICROSOFT ENTRA ID -> uses the store's endpoint URL plus a
///      (Azure AD / RBAC)     <see cref="TokenCredential"/>. The identity must be
///                            granted the "App Configuration Data Reader" role.
///                            Every other method below is a flavour of this.
///
/// This factory only deals with case (2): it builds the right credential.
/// Program.cs decides between case (1) and case (2).
/// </summary>
public static class AppConfigCredentialFactory
{
    public static TokenCredential Create(AppConfigOptions options, ILogger logger)
    {
        logger.LogInformation("Building Azure App Configuration credential for method: {Method}", options.AuthMethod);

        return options.AuthMethod switch
        {
            // -----------------------------------------------------------------
            // DefaultAzureCredential — the recommended default.
            //
            // It tries an ordered chain of credentials and uses the first that
            // works. In Azure it lands on Managed Identity; on a dev machine it
            // falls back to Azure CLI / Visual Studio / VS Code sign-in. This
            // means the SAME code runs locally and in the cloud with no changes.
            //
            // Chain (abbreviated): Environment vars -> Workload Identity ->
            // Managed Identity -> Azure CLI -> Azure Developer CLI -> VS / VS Code.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.DefaultAzureCredential => new DefaultAzureCredential(
                new DefaultAzureCredentialOptions
                {
                    // When set, pins the user-assigned managed identity that the
                    // Managed Identity link in the chain should use. Harmless to
                    // leave null for system-assigned identity / local dev.
                    ManagedIdentityClientId = NullIfBlank(options.ManagedIdentityClientId),
                }),

            // -----------------------------------------------------------------
            // System-assigned Managed Identity — runs only inside Azure
            // (App Service, Container Apps, Functions, VM, AKS, etc.).
            // No secrets anywhere: the platform issues tokens for the resource's
            // own identity. This is the production sweet spot.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.SystemAssignedManagedIdentity =>
                new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),

            // -----------------------------------------------------------------
            // User-assigned Managed Identity — a standalone identity you create
            // once and attach to many resources. Requires its client id.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.UserAssignedManagedIdentity =>
                new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(
                    RequireValue(options.ManagedIdentityClientId, nameof(options.ManagedIdentityClientId)))),

            // -----------------------------------------------------------------
            // Service Principal with a client secret — an app registration in
            // Entra ID with TenantId + ClientId + ClientSecret. Works anywhere,
            // but you now own a secret (rotation, leakage risk). Prefer Managed
            // Identity in Azure; use this for CI or non-Azure hosts.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.ServicePrincipalSecret => new ClientSecretCredential(
                RequireValue(options.TenantId, nameof(options.TenantId)),
                RequireValue(options.ClientId, nameof(options.ClientId)),
                RequireValue(options.ClientSecret, nameof(options.ClientSecret))),

            // -----------------------------------------------------------------
            // Service Principal with a certificate — same as above but the
            // credential is a cert instead of a shared secret. Stronger, since
            // the private key never leaves the host.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.ServicePrincipalCertificate => new ClientCertificateCredential(
                RequireValue(options.TenantId, nameof(options.TenantId)),
                RequireValue(options.ClientId, nameof(options.ClientId)),
                RequireValue(options.ClientCertificatePath, nameof(options.ClientCertificatePath))),

            // -----------------------------------------------------------------
            // Azure CLI — uses whoever is signed in via `az login`. Great for
            // local development; never use it on a server.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.AzureCli => new AzureCliCredential(),

            // -----------------------------------------------------------------
            // Workload Identity — the Kubernetes/AKS pattern. A federated
            // credential trades a projected service-account token for an Entra
            // token. The required values are injected as env vars by AKS.
            // -----------------------------------------------------------------
            AppConfigAuthMethod.WorkloadIdentity =>
                new WorkloadIdentityCredential(),

            _ => throw new InvalidOperationException(
                $"Unsupported AppConfig auth method: {options.AuthMethod}")
        };
    }

    private static string RequireValue(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"AppConfig:{name} must be set for the selected authentication method.")
            : value;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
