namespace AppConfigDemo.AppConfigAuth;

/// <summary>
/// The supported ways an ASP.NET Core Web API can authenticate to
/// Azure App Configuration. Selected via the "AppConfig:AuthMethod" setting.
/// </summary>
public enum AppConfigAuthMethod
{
    /// <summary>Access-key connection string. No Entra ID / RBAC involved.</summary>
    ConnectionString,

    /// <summary>Recommended: tries Managed Identity in Azure, dev sign-in locally.</summary>
    DefaultAzureCredential,

    /// <summary>System-assigned managed identity (Azure-hosted only).</summary>
    SystemAssignedManagedIdentity,

    /// <summary>User-assigned managed identity (needs ManagedIdentityClientId).</summary>
    UserAssignedManagedIdentity,

    /// <summary>Entra app registration with a client secret.</summary>
    ServicePrincipalSecret,

    /// <summary>Entra app registration with a client certificate.</summary>
    ServicePrincipalCertificate,

    /// <summary>Local dev: whoever is signed in via `az login`.</summary>
    AzureCli,

    /// <summary>AKS federated workload identity.</summary>
    WorkloadIdentity
}

/// <summary>
/// Strongly-typed view over the "AppConfig" configuration section.
/// </summary>
public sealed class AppConfigOptions
{
    public const string SectionName = "AppConfig";

    /// <summary>Which authentication method to use. Defaults to the recommended one.</summary>
    public AppConfigAuthMethod AuthMethod { get; set; } = AppConfigAuthMethod.DefaultAzureCredential;

    /// <summary>The store endpoint, e.g. https://my-store.azconfig.io (Entra ID methods).</summary>
    public string? Endpoint { get; set; }

    /// <summary>The access-key connection string (ConnectionString method only).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Client id of a user-assigned managed identity.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Entra tenant id (service principal methods).</summary>
    public string? TenantId { get; set; }

    /// <summary>Entra app registration (client) id (service principal methods).</summary>
    public string? ClientId { get; set; }

    /// <summary>App registration client secret (ServicePrincipalSecret).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Path to a .pfx certificate (ServicePrincipalCertificate).</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>The Entra sentinel key used to trigger a configuration refresh.</summary>
    public string SentinelKey { get; set; } = "Sentinel";

    /// <summary>How often (seconds) to poll for changed config / feature flags.</summary>
    public int RefreshIntervalSeconds { get; set; } = 30;

    /// <summary>Optional label filter, e.g. "Development" or "Production".</summary>
    public string? Label { get; set; }
}
