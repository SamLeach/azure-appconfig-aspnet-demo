terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}

provider "azurerm" {
  features {}
  # Authentication is picked up from your environment:
  #   - `az login`            (local development)        <- easiest
  #   - ARM_* environment vars (service principal / CI)
  #   - Managed identity       (when running in Azure)
  # Set `subscription_id` here or via ARM_SUBSCRIPTION_ID if you have more than one.
  subscription_id = var.subscription_id != "" ? var.subscription_id : null
}

# The identity Terraform itself is running as — used to grant ourselves the
# data-plane role needed to seed keys/feature flags.
data "azurerm_client_config" "current" {}
