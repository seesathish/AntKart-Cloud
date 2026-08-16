# =============================================================================
# Networking module — VNet, subnets, NSGs
# =============================================================================
# Builds the private network for the environment: one virtual network, a set of
# non-overlapping subnets, and one Network Security Group per subnet with an
# explicit deny-by-default baseline. Everything is driven by inputs, so the same
# module produces a structurally identical network in any environment.

# --- Virtual Network ---------------------------------------------------------
# The private address space everything else is carved from. Subnets below must
# all fit within address_space and must not overlap each other.
resource "azurerm_virtual_network" "this" {
  name                = var.vnet_name
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = var.vnet_address_space
  tags                = var.tags
}

# --- Subnets -----------------------------------------------------------------
# One subnet per entry in var.subnets, created with for_each so the set of
# subnets is data (the environment's map), not copy-pasted resource blocks.
resource "azurerm_subnet" "this" {
  for_each = var.subnets

  name                 = each.key
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = each.value.address_prefixes

  # Optional per-subnet settings (defaulted in variables.tf).
  service_endpoints                 = each.value.service_endpoints
  private_endpoint_network_policies = each.value.private_endpoint_network_policies
}

# --- Network Security Groups -------------------------------------------------
# One NSG per subnet (same for_each keys), each carrying an explicit
# deny-by-default baseline. NSGs are stateful: return traffic for an allowed
# inbound flow is permitted automatically, so only inbound rules are needed here.
resource "azurerm_network_security_group" "this" {
  for_each = var.subnets

  name                = "nsg-${each.key}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  # 1. Allow traffic within the VNet (subnet-to-subnet and intra-subnet), so the
  #    services that make up the platform can talk to each other privately.
  security_rule {
    name                       = "AllowVnetInbound"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "VirtualNetwork"
    destination_address_prefix = "VirtualNetwork"
  }

  # 2. Allow the Azure Load Balancer to reach the subnet for health probes —
  #    required by load-balanced services and the Kubernetes load balancer.
  security_rule {
    name                       = "AllowAzureLoadBalancerInbound"
    priority                   = 200
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "AzureLoadBalancer"
    destination_address_prefix = "*"
  }

  # 3. (Conditional) Allow inbound HTTP (80) and HTTPS (443) from the Internet —
  #    rendered ONLY on subnets flagged allow_internet_ingress (the subnet hosting
  #    the internet-facing ingress controller / load balancer). Priorities 300/310
  #    sit BELOW DenyAllInbound (4096), so this traffic is permitted before the
  #    default deny.
  #
  #    WHY these rules are needed: this is a BRING-YOUR-OWN VNet with a
  #    CUSTOMER-MANAGED NSG. When the NSG on the subnet is not AKS-managed, AKS
  #    (cloud-controller-manager) will NOT automatically add the LoadBalancer allow
  #    rules for a public Service. So client traffic from the internet arrives with
  #    the Internet service tag, matches nothing above, and is dropped by
  #    DenyAllInbound — the LB gets a public IP but every request times out (and the
  #    Let's Encrypt HTTP-01 challenge fails). The AzureLoadBalancer rule (2) only
  #    permits Azure's health probes, NOT client traffic, so it does not cover this.
  #    These two rules are the customer-managed-NSG equivalent of what AKS would
  #    otherwise add for you.
  dynamic "security_rule" {
    for_each = each.value.allow_internet_ingress ? [1] : []
    content {
      name                       = "AllowInternetHttpInbound"
      priority                   = 300
      direction                  = "Inbound"
      access                     = "Allow"
      protocol                   = "Tcp"
      source_port_range          = "*"
      destination_port_range     = "80"
      source_address_prefix      = "Internet"
      destination_address_prefix = "*"
    }
  }

  dynamic "security_rule" {
    for_each = each.value.allow_internet_ingress ? [1] : []
    content {
      name                       = "AllowInternetHttpsInbound"
      priority                   = 310
      direction                  = "Inbound"
      access                     = "Allow"
      protocol                   = "Tcp"
      source_port_range          = "*"
      destination_port_range     = "443"
      source_address_prefix      = "Internet"
      destination_address_prefix = "*"
    }
  }

  # 4. Deny everything else inbound — the default-deny baseline. Service-specific
  #    allow rules (for example, inbound 443 to the gateway subnet) are layered
  #    on top as those services are introduced.
  security_rule {
    name                       = "DenyAllInbound"
    priority                   = 4096
    direction                  = "Inbound"
    access                     = "Deny"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }
}

# --- NSG <-> Subnet associations --------------------------------------------
# Attach each NSG to its matching subnet. Without the association the NSG exists
# but filters nothing.
resource "azurerm_subnet_network_security_group_association" "this" {
  for_each = var.subnets

  subnet_id                 = azurerm_subnet.this[each.key].id
  network_security_group_id = azurerm_network_security_group.this[each.key].id
}
