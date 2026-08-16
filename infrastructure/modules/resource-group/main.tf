# =============================================================================
# Resource Group module — resource
# =============================================================================
# A single azurerm_resource_group built entirely from the module's inputs. No
# value is hardcoded here: the environment supplies name, location, and tags,
# so this one definition serves every environment.

resource "azurerm_resource_group" "this" {
  name     = var.name
  location = var.location
  tags     = var.tags

  lifecycle {
    # prevent_destroy guards this stateful container. A resource group is the
    # parent of every resource in the environment, so an accidental destroy
    # would take them ALL with it. Terraform will refuse any plan that would
    # delete this resource group until this guard is intentionally removed.
    prevent_destroy = true
  }
}
