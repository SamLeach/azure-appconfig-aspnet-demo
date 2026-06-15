variable "subscription_id" {
  type        = string
  description = "Azure subscription id. Leave blank to use the one from `az account show` / ARM_SUBSCRIPTION_ID."
  default     = ""
}

variable "location" {
  type        = string
  description = "Azure region for all resources."
  default     = "uksouth"
}

variable "resource_group_name" {
  type        = string
  description = "Name of the resource group to create."
  default     = "rg-appconfig-demo"
}

variable "name_prefix" {
  type        = string
  description = "Prefix used to name resources. A random suffix is appended for global uniqueness."
  default     = "appconfigdemo"
}

variable "sku" {
  type        = string
  description = "App Configuration SKU: free, developer, standard, or premium. 'free' allows only one store per subscription."
  default     = "standard"

  validation {
    condition     = contains(["free", "developer", "standard", "premium"], var.sku)
    error_message = "sku must be one of: free, developer, standard, premium."
  }
}

variable "deploy_app_service" {
  type        = bool
  description = "Also deploy a Linux App Service with a system-assigned managed identity wired to App Configuration. Set true to demo Managed Identity end-to-end in Azure."
  default     = false
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default = {
    project = "azure-app-config-demo"
    managed = "terraform"
  }
}
