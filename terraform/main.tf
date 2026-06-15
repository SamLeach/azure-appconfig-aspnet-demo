resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

locals {
  suffix = random_string.suffix.result
}

# ---------------------------------------------------------------------------
# Resource group
# ---------------------------------------------------------------------------
resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# ---------------------------------------------------------------------------
# Azure App Configuration store
#   - local_auth_enabled = true keeps access keys (connection strings) usable
#     so the demo can show that method too. In production you'd often set this
#     to false to force Entra ID / RBAC only.
# ---------------------------------------------------------------------------
resource "azurerm_app_configuration" "appconfig" {
  name                = "${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku                 = var.sku
  local_auth_enabled  = true
  tags                = var.tags
}

# ---------------------------------------------------------------------------
# User-assigned managed identity (one of the auth methods the app demonstrates).
# Attach this to any Azure compute (App Service, Container Apps, AKS, VM...).
# ---------------------------------------------------------------------------
resource "azurerm_user_assigned_identity" "app" {
  name                = "id-${var.name_prefix}-${local.suffix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = var.tags
}

# ---------------------------------------------------------------------------
# RBAC role assignments (data plane).
#
# Reading config/flags over Entra ID requires "App Configuration Data Reader".
# Writing requires "App Configuration Data Owner".
# ---------------------------------------------------------------------------

# Let the user-assigned identity READ config + flags.
resource "azurerm_role_assignment" "uami_data_reader" {
  scope                = azurerm_app_configuration.appconfig.id
  role_definition_name = "App Configuration Data Reader"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

# Let YOU (whoever runs Terraform / `az login`) READ over Entra ID, so the
# DefaultAzureCredential / AzureCli methods work from your dev machine.
resource "azurerm_role_assignment" "me_data_reader" {
  scope                = azurerm_app_configuration.appconfig.id
  role_definition_name = "App Configuration Data Reader"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Let Terraform WRITE the seed keys/flags below (data-plane owner).
resource "azurerm_role_assignment" "me_data_owner" {
  scope                = azurerm_app_configuration.appconfig.id
  role_definition_name = "App Configuration Data Owner"
  principal_id         = data.azurerm_client_config.current.object_id
}

# RBAC assignments are eventually consistent; give them a moment to propagate
# before we try to write keys, otherwise the data-plane calls 403.
resource "time_sleep" "wait_for_rbac" {
  depends_on      = [azurerm_role_assignment.me_data_owner]
  create_duration = "60s"
}

# ---------------------------------------------------------------------------
# Seed configuration values. The app loads keys prefixed "AppConfigDemo:".
# ---------------------------------------------------------------------------
resource "azurerm_app_configuration_key" "message" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  key                    = "AppConfigDemo:Message"
  value                  = "Hello from Azure App Configuration!"
  depends_on             = [time_sleep.wait_for_rbac]
}

resource "azurerm_app_configuration_key" "greeting" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  key                    = "AppConfigDemo:Greeting"
  value                  = "Welcome to the App Config + Feature Flags demo"
  depends_on             = [time_sleep.wait_for_rbac]
}

resource "azurerm_app_configuration_key" "environment" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  key                    = "AppConfigDemo:Environment"
  value                  = "azure"
  depends_on             = [time_sleep.wait_for_rbac]
}

# The sentinel key the app watches for dynamic refresh. Bump its value to push
# all config/flag changes to running apps without a restart.
resource "azurerm_app_configuration_key" "sentinel" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  key                    = "Sentinel"
  value                  = "1"
  depends_on             = [time_sleep.wait_for_rbac]
}

# ---------------------------------------------------------------------------
# Seed feature flags.
# ---------------------------------------------------------------------------
resource "azurerm_app_configuration_feature" "beta" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  name                   = "BetaFeature"
  description            = "Gates the /api/beta endpoint. Enabled in this demo."
  enabled                = true
  depends_on             = [time_sleep.wait_for_rbac]
}

resource "azurerm_app_configuration_feature" "coming_soon" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  name                   = "ComingSoon"
  description            = "A flag that is currently off."
  enabled                = false
  depends_on             = [time_sleep.wait_for_rbac]
}

# A flag rolled out to 50% of evaluations, to show targeting filters.
resource "azurerm_app_configuration_feature" "gradual_rollout" {
  configuration_store_id = azurerm_app_configuration.appconfig.id
  name                   = "GradualRollout"
  description            = "Enabled for ~50% of evaluations via a percentage filter."
  enabled                = true

  percentage_filter_value = 50

  depends_on = [time_sleep.wait_for_rbac]
}
