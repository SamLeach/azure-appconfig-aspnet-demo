# ---------------------------------------------------------------------------
# OPTIONAL: a Linux App Service that hosts the API with a SYSTEM-ASSIGNED
# managed identity wired to App Configuration. This makes the Managed Identity
# auth method real (managed identity only works when running inside Azure).
#
# Enable with:  deploy_app_service = true
#
# Deploy the code afterwards, e.g.:
#   dotnet publish ../src/AppConfigDemo -c Release -o publish
#   Compress-Archive -Path publish/* -DestinationPath app.zip -Force
#   az webapp deploy -g <rg> -n $(terraform output -raw web_app_name) --src-path app.zip --type zip
# ---------------------------------------------------------------------------

resource "azurerm_service_plan" "plan" {
  count               = var.deploy_app_service ? 1 : 0
  name                = "plan-${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = "ukwest" # uksouth has 0 App Service VM quota on this sub; ukwest has quota.
  os_type             = "Linux"
  sku_name            = "F1" # Free tier: no dedicated-VM quota required, $0 cost.
  tags                = var.tags
}

resource "azurerm_linux_web_app" "app" {
  count               = var.deploy_app_service ? 1 : 0
  name                = "app-${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = "ukwest" # Must match the plan's region.
  service_plan_id     = azurerm_service_plan.plan[0].id
  https_only          = true # Reject plaintext HTTP (azurerm defaults this to false).
  tags                = var.tags

  # Attach the USER-ASSIGNED managed identity (azurerm_user_assigned_identity.app).
  # The app authenticates as THIS identity — no secrets, no system-assigned identity.
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  site_config {
    always_on = false # F1 free tier does not support always_on.
    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = {
    # Double underscore maps to the "AppConfig:" config section in .NET.
    "AppConfig__AuthMethod"              = "UserAssignedManagedIdentity"
    "AppConfig__Endpoint"                = azurerm_app_configuration.appconfig.endpoint
    "AppConfig__ManagedIdentityClientId" = azurerm_user_assigned_identity.app.client_id
    "ASPNETCORE_ENVIRONMENT"             = "Production"
  }
}

# No role assignment needed here: the user-assigned identity is already granted
# "App Configuration Data Reader" by azurerm_role_assignment.uami_data_reader
# in main.tf. That single grant is the ONLY data access this web app has.
