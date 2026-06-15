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
#   az webapp deploy -g <rg> -n <app-name> --src-path app.zip --type zip
# ---------------------------------------------------------------------------

resource "azurerm_service_plan" "plan" {
  count               = var.deploy_app_service ? 1 : 0
  name                = "plan-${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = "Linux"
  sku_name            = "B1"
  tags                = var.tags
}

resource "azurerm_linux_web_app" "app" {
  count               = var.deploy_app_service ? 1 : 0
  name                = "app-${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.plan[0].id
  tags                = var.tags

  # Turn on a system-assigned managed identity for this web app.
  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = {
    # Double underscore maps to the "AppConfig:" config section in .NET.
    "AppConfig__AuthMethod"  = "SystemAssignedManagedIdentity"
    "AppConfig__Endpoint"    = azurerm_app_configuration.appconfig.endpoint
    "ASPNETCORE_ENVIRONMENT" = "Production"
  }
}

# Grant the web app's system-assigned identity read access to config + flags.
resource "azurerm_role_assignment" "app_data_reader" {
  count                = var.deploy_app_service ? 1 : 0
  scope                = azurerm_app_configuration.appconfig.id
  role_definition_name = "App Configuration Data Reader"
  principal_id         = azurerm_linux_web_app.app[0].identity[0].principal_id
}
