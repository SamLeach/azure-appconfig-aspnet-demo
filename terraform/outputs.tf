output "app_configuration_name" {
  description = "Name of the App Configuration store."
  value       = azurerm_app_configuration.appconfig.name
}

output "app_configuration_endpoint" {
  description = "Endpoint URL. Put this in AppConfig:Endpoint for the Entra ID auth methods."
  value       = azurerm_app_configuration.appconfig.endpoint
}

output "app_configuration_connection_string" {
  description = "Read/write access-key connection string (for the ConnectionString auth method)."
  value       = azurerm_app_configuration.appconfig.primary_read_key[0].connection_string
  sensitive   = true
}

output "user_assigned_identity_client_id" {
  description = "Client id of the user-assigned managed identity (AppConfig:ManagedIdentityClientId)."
  value       = azurerm_user_assigned_identity.app.client_id
}

output "resource_group_name" {
  description = "Resource group that was created."
  value       = azurerm_resource_group.rg.name
}

output "web_app_default_hostname" {
  description = "Hostname of the optional App Service (only when deploy_app_service = true)."
  value       = var.deploy_app_service ? azurerm_linux_web_app.app[0].default_hostname : null
}
