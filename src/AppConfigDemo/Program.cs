using AppConfigDemo.AppConfigAuth;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Read our "AppConfig" settings (auth method, endpoint, etc.) from the
//    local configuration (appsettings.json / env vars / user-secrets).
// ---------------------------------------------------------------------------
var appConfigOptions = builder.Configuration
    .GetSection(AppConfigOptions.SectionName)
    .Get<AppConfigOptions>() ?? new AppConfigOptions();

builder.Services.Configure<AppConfigOptions>(
    builder.Configuration.GetSection(AppConfigOptions.SectionName));

// A bootstrap logger so we can see what's happening before the host is built.
using var bootstrapFactory = LoggerFactory.Create(b => b.AddConsole());
var log = bootstrapFactory.CreateLogger("AppConfigBootstrap");

// ---------------------------------------------------------------------------
// 2. Wire up Azure App Configuration.
//
//    There are two top-level connection styles:
//      (a) Connection string  -> options.Connect(connectionString)
//      (b) Entra ID + endpoint -> options.Connect(endpointUri, tokenCredential)
//
//    Everything in AppConfigCredentialFactory is style (b).
//
//    The app still boots if nothing is configured yet (e.g. before you've run
//    Terraform), so you can explore the code without Azure. It just won't have
//    any remote config or feature flags.
// ---------------------------------------------------------------------------
var connectedToAzure = false;

bool hasConnectionString = appConfigOptions.AuthMethod == AppConfigAuthMethod.ConnectionString
    && !string.IsNullOrWhiteSpace(appConfigOptions.ConnectionString);
bool hasEndpoint = !string.IsNullOrWhiteSpace(appConfigOptions.Endpoint);

if (hasConnectionString || hasEndpoint)
{
    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        // ---- Style (a): access-key connection string --------------------
        // Gate on hasConnectionString (not just the auth method) so a blank
        // connection string with an Endpoint set falls through to the Entra ID
        // path instead of calling Connect("") and crashing at startup.
        if (hasConnectionString)
        {
            log.LogInformation("Connecting to App Configuration with a connection string (access key).");
            options.Connect(appConfigOptions.ConnectionString);
        }
        // ---- Style (b): Microsoft Entra ID + a TokenCredential ----------
        else
        {
            log.LogInformation("Connecting to App Configuration at {Endpoint} via Entra ID ({Method}).",
                appConfigOptions.Endpoint, appConfigOptions.AuthMethod);

            var credential = AppConfigCredentialFactory.Create(appConfigOptions, log);
            options.Connect(new Uri(appConfigOptions.Endpoint!), credential);
        }

        // Only load keys that start with this prefix, and strip it. Optionally
        // filter by label (e.g. per-environment values). A blank label means
        // "no label" — pass null, since the SDK's no-label selector is null
        // (LabelFilter.Null), not the empty string.
        var labelFilter = string.IsNullOrWhiteSpace(appConfigOptions.Label)
            ? null
            : appConfigOptions.Label;

        options.Select(keyFilter: "AppConfigDemo:*", labelFilter: labelFilter)
               .TrimKeyPrefix("AppConfigDemo:");

        // Dynamic refresh: re-read changed values without restarting. We watch a
        // single "sentinel" key — bump it in the portal/Terraform to push all
        // changes at once.
        options.ConfigureRefresh(refresh =>
        {
            refresh.Register(appConfigOptions.SentinelKey, refreshAll: true)
                   .SetRefreshInterval(TimeSpan.FromSeconds(appConfigOptions.RefreshIntervalSeconds));
        });

        // Pull feature flags from the store too (also dynamically refreshed).
        options.UseFeatureFlags(flags =>
        {
            flags.SetRefreshInterval(TimeSpan.FromSeconds(appConfigOptions.RefreshIntervalSeconds));
        });
    });

    // Required for dynamic refresh to actually fire on incoming requests.
    builder.Services.AddAzureAppConfiguration();
    connectedToAzure = true;
}
else
{
    log.LogWarning(
        "No App Configuration endpoint/connection string set — running in LOCAL ONLY mode. " +
        "Set AppConfig:Endpoint (after running Terraform) to connect.");
}

// ---------------------------------------------------------------------------
// 3. Feature management (reads the feature flags loaded above).
// ---------------------------------------------------------------------------
builder.Services.AddFeatureManagement();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

// Activates dynamic refresh middleware so config/flag changes are picked up.
if (connectedToAzure)
{
    app.UseAzureAppConfiguration();
}

// ---------------------------------------------------------------------------
// Endpoints — a tiny tour of what's now wired up.
// ---------------------------------------------------------------------------

app.MapGet("/", () => Results.Ok(new
{
    service = "Azure App Configuration + Feature Flags demo",
    connectedToAzure,
    authMethod = appConfigOptions.AuthMethod.ToString(),
    try_these = new[] { "/auth/info", "/config", "/features", "/api/beta", "/health" }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Shows HOW the app authenticated — the heart of this demo.
app.MapGet("/auth/info", (Microsoft.Extensions.Options.IOptions<AppConfigOptions> opts) =>
{
    var o = opts.Value;
    return Results.Ok(new
    {
        connectedToAzure,
        authMethod = o.AuthMethod.ToString(),
        connectionStyle = o.AuthMethod == AppConfigAuthMethod.ConnectionString
            ? "Access-key connection string"
            : "Microsoft Entra ID (RBAC) + TokenCredential",
        endpoint = o.Endpoint,
        usesSecrets = o.AuthMethod is AppConfigAuthMethod.ConnectionString
            or AppConfigAuthMethod.ServicePrincipalSecret,
        requiredRbacRole = o.AuthMethod == AppConfigAuthMethod.ConnectionString
            ? "(none — access key)"
            : "App Configuration Data Reader",
        managedIdentityClientId = o.ManagedIdentityClientId
    });
});

// Reads configuration values that came from App Configuration (the
// "AppConfigDemo:" prefix was trimmed, so e.g. "AppConfigDemo:Message" -> "Message").
app.MapGet("/config", (IConfiguration config) => Results.Ok(new
{
    message = config["Message"] ?? "(not set — is App Configuration connected?)",
    greeting = config["Greeting"] ?? "(not set)",
    environment = config["Environment"] ?? "(not set)"
}));

// Lists the feature flags and whether each is currently enabled.
app.MapGet("/features", async (IFeatureManager features) =>
{
    var result = new Dictionary<string, bool>();
    await foreach (var name in features.GetFeatureNamesAsync())
    {
        result[name] = await features.IsEnabledAsync(name);
    }
    return Results.Ok(new { featureFlags = result });
});

// A feature-gated endpoint: returns 200 only when the "BetaFeature" flag is on.
app.MapGet("/api/beta", async (IFeatureManager features) =>
{
    if (!await features.IsEnabledAsync("BetaFeature"))
    {
        return Results.StatusCode(StatusCodes.Status404NotFound);
    }
    return Results.Ok(new { message = "🎉 Beta feature is enabled!" });
});

app.Run();
