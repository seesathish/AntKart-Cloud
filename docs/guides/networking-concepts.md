# Networking & Kubernetes Concepts — Primer

A first-principles reference for the networking and Kubernetes ideas behind the platform's cloud infrastructure. It explains the concepts first, then shows how they shape the way the network is sized. No prior cloud-networking knowledge is assumed.

---

## 1. IP Addressing & CIDR

**What an IP address is.** An IPv4 address is a **32-bit** number that identifies a device on a network. Those 32 bits are written as **4 octets × 8 bits = 32 bits**, where each octet is one number from 0–255, separated by dots.

Take the sample address `10.0.4.17`:

| Octet | 1 | 2 | 3 | 4 |
|-------|---|---|---|---|
| Decimal | 10 | 0 | 4 | 17 |
| Binary (8 bits each) | `00001010` | `00000000` | `00000100` | `00010001` |

Four octets of 8 bits each = 32 bits in total. Every machine that talks on a network needs one of these addresses.

**What CIDR notation means.** A network is a *range* of addresses, written as an address followed by a slash and a number — for example `10.0.0.0/16`. The number after the slash, the **prefix length** `/n`, says that the **first `n` bits are the fixed network prefix** (the same for every address in the range) and the **remaining `32 − n` bits are free for hosts** (they vary, one value per address). So:

- **Total addresses in a block = `2^(32 − n)`.**
- A bigger host portion (smaller `n`) means more addresses.

**Worked example — `/24`.** In `10.0.4.0/24`, the prefix is 24 bits — that is the **first three octets** (`10.0.4`), fixed — leaving the **last octet (8 bits)** for hosts. 8 host bits → `2^8 = 256` addresses, the range `10.0.4.0` through `10.0.4.255`.

**Worked example — `/22`.** In `10.0.0.0/22`, the prefix is 22 bits, leaving **`32 − 22 = 10` host bits** → `2^10 = 1,024` addresses. Those 10 host bits are the **whole 4th octet (8 bits) plus the bottom 2 bits of the 3rd octet** — so the 3rd octet counts `0, 1, 2, 3` (`2^2 = 4` values) and the 4th octet runs `0–255`: `4 × 256 = 1,024`. The full range of `10.0.0.0/22` is therefore:

- **Network address (first):** `10.0.0.0`
- **Broadcast address (last):** `10.0.3.255`
- It spans **four consecutive `/24` blocks** — `10.0.0.x`, `10.0.1.x`, `10.0.2.x`, `10.0.3.x` (the 3rd octet `.0` through `.3`).

**Common block sizes.** Azure reserves 5 addresses per subnet (explained below), so usable = total − 5:

| CIDR | Host bits (32 − n) | Total addresses (2^(32−n)) | Usable in Azure (− 5) |
|------|--------------------|----------------------------|-----------------------|
| `/16` | 16 | 65,536 | 65,531 |
| `/22` | 10 | 1,024 | 1,019 |
| `/24` | 8 | 256 | 251 |
| `/26` | 6 | 64 | 59 |
| `/27` | 5 | 32 | 27 |

> **Rule of thumb:** a **smaller slash number means a bigger block**. `/16` is far larger than `/24`. Each step *down* in the prefix (e.g. `/24` → `/22`) roughly *quadruples* the size (two more host bits = 4× the addresses).

---

## 2. The 5 Reserved Addresses (per subnet)

Azure reserves **5 addresses in every subnet** — this is **per subnet**, not per VNet, so a subnet's usable count is always its total minus 5. For a subnet `x.y.z.0/24` they are:

| Reserved address | Example (`x.y.z.0/24`) | Why |
|------------------|------------------------|-----|
| **Network address** (the first) | `x.y.z.0` | Identifies the subnet itself; not assignable to a host. |
| **Default gateway** | `x.y.z.1` | The router the subnet uses to reach other subnets and the internet. |
| **Azure DNS mapping** | `x.y.z.2` | Reserved by the platform to map to the Azure-provided DNS service. |
| **Azure DNS mapping** | `x.y.z.3` | Second address reserved for Azure-managed DNS. |
| **Broadcast address** (the last) | `x.y.z.255` | The all-hosts broadcast address; not assignable. |

So a `/24` has 256 total but **251 usable**, a `/27` has 32 total but **27 usable**, and so on.

---

## 3. VNet, Subnet, and NSG

**Virtual Network (VNet).** A VNet is **your own isolated, private network in Azure**, defined by an **address space you choose** (commonly a `/16`). Resources placed inside it **communicate privately** over that address space without traversing the public internet, and nothing outside can reach in unless you explicitly allow it. A VNet **does not overlap** with other networks you connect it to (overlapping ranges cannot be routed between). Because changing a VNet's range later means **re-addressing everything inside it — disruptive and risky — you size it generously up front**, leaving plenty of room to add subnets and grow.

**Subnet.** A subnet is a **non-overlapping slice of the VNet's range, dedicated to one workload** (for example: the Kubernetes cluster, the private endpoints, the gateway). Each subnet's range must **fit within the VNet** and **not overlap any other subnet** in it. Workloads are separated into different subnets for good reasons:

- **Isolation** — a problem or compromise in one subnet is contained, not free to roam the whole network.
- **Distinct security rules** — each subnet can have its own firewall (NSG) rules suited to what it hosts.
- **Distinct sizing** — each subnet is sized to its own IP demand (the cluster needs many addresses; an endpoints subnet needs few).

**Network Security Group (NSG).** An NSG is a **stateful firewall attached to a subnet** (or to an individual network interface). It holds an ordered list of **allow/deny rules**, each matching on **source, destination, port, and protocol**, evaluated in **priority** order (lower number wins) until one matches.

> **"Stateful"** means the NSG tracks connections: if you **allow an inbound** request, the **matching return traffic is automatically allowed** — you do **not** write a separate outbound rule for the response. (A *stateless* firewall would need an explicit rule for each direction.)

**A simple concrete example.** To expose a web endpoint over HTTPS and nothing else, a subnet's NSG might say:

| Priority | Direction | Source | Destination | Port | Protocol | Action |
|----------|-----------|--------|-------------|------|----------|--------|
| 100 | Inbound | Internet | Subnet | 443 | TCP | **Allow** |
| 4096 | Inbound | Any | Any | Any | Any | **Deny** |

Rule 100 allows inbound HTTPS on port 443; the low-priority catch-all at 4096 denies everything else. Because the NSG is stateful, the responses to those allowed 443 requests flow back out automatically — no extra outbound rule needed.

---

## 4. Kubernetes Fundamentals (smallest to largest)

It helps to build the picture from the smallest piece up:

- **Container** — a packaged application plus everything it needs to run (runtime, libraries, config), isolated from other processes. It is the unit you build and ship.
- **Pod** — the **smallest deployable unit** in Kubernetes. A pod wraps one container (occasionally a few tightly-coupled ones that must share a fate). **Each pod gets its own IP address.** Pods are disposable: they are created and destroyed as workloads scale or move.
- **Node** — a **virtual machine that runs many pods**. It provides the CPU, memory, and the agent (kubelet) and container runtime that actually launch the pods scheduled onto it.
- **Cluster** — **all the nodes together, plus the control plane** (the API server, scheduler, and controllers) that decides what runs where and keeps the desired state. The control plane manages; the nodes do the work.
- **Service** — a **stable virtual address (and DNS name) that routes to the healthy pods** behind it. Because pods come and go (and change IPs), clients talk to the *service*, which load-balances across the current healthy pods. Services use a **separate internal IP range** (the cluster "service" range), distinct from pod IPs.

**Horizontal scaling.** To handle more load, Kubernetes adds **more pods** (the Horizontal Pod Autoscaler reacts to metrics like CPU). When the existing nodes run out of room to place new pods, the **cluster autoscaler adds more nodes**. Scaling out (more copies) is preferred over scaling up (bigger machines).

---

## 5. Azure CNI & IP-Consumption Math

How pods get their IPs determines how big the cluster's subnet must be.

**Traditional Azure CNI.** With the traditional Azure CNI networking mode, **every pod takes a real IP from the VNet subnet** — pods are first-class citizens on the VNet, directly addressable. To place pods quickly, **each node pre-reserves a block of subnet IPs up front**: by default room for about **30 pods per node, plus 1 IP for the node itself ≈ 31 IPs reserved per node** — whether or not those pods exist yet.

**The worked math.** Because each node reserves ~31 IPs:

| Nodes | Reserved IPs (≈ nodes × 31) |
|-------|-----------------------------|
| 3 | ≈ 93 |
| 10 | ≈ 310 |

On top of that, leave **surge / headroom** for rolling upgrades and scale-out (during an upgrade the cluster temporarily runs extra nodes). So a cluster that may grow to ~10 nodes needs well over 310 addresses once headroom is included.

This is why the **AKS subnet is sized `/22` (1,019 usable)** — comfortably above the ~310 + surge — while ordinary subnets that only host a handful of resources are `/24` or smaller.

**Azure CNI Overlay (alternative).** In CNI **Overlay** mode, **pod IPs come from a separate overlay address range**, not the VNet subnet. The VNet subnet then only needs enough IPs for the **nodes** (and a few platform endpoints), not every pod — making the subnet **far more IP-efficient** and removing the per-node block reservation as the sizing driver. It is the better choice when VNet address space is scarce or pod counts are very high.

---

## 6. The Network We Build

Putting the concepts together, the platform's address plan is:

| Network | Range / size | Addresses | NSG | Why this size |
|---------|--------------|-----------|-----|---------------|
| **VNet** | `10.0.0.0/16` | 65,536 | — | A large private space to carve every subnet from, with ample room to grow without re-addressing. |
| **AKS subnet** | `/22` | 1,024 | yes | The big one: with traditional Azure CNI **each pod consumes a subnet IP and each node pre-reserves a block of them**, plus upgrade/scale surge — so it needs hundreds to ~1,000 addresses. |
| **Private endpoints subnet** | `/24` | 256 | yes | Each private endpoint to a managed service consumes only one IP; a few hundred addresses is plenty. |
| **Gateway subnet** | `/27` | 32 | yes | The gateway/edge front door needs only a small, dedicated slice. |

Every subnet sits **inside the VNet's `10.0.0.0/16`**, the ranges **do not overlap**, and each carries **its own NSG** so the cluster, the private endpoints, and the gateway are firewalled to exactly the traffic they should see.

The headline point: **the AKS subnet is the large one (`/22`)** because, under traditional Azure CNI, pod IPs and per-node reservations come straight out of it. The supporting subnets are small because they hold only a few endpoints — **size each subnet to its real IP demand**.

---

This primer supports the **Networking** step of the [Infrastructure Guide](infrastructure-guide.md), where these concepts are turned into the actual VNet, subnets, and NSGs.

---

**Navigation:** [← Development Guide](../../DevelopmentGuide.md) · **Applied in:** [Infrastructure Guide](infrastructure-guide.md) · **Related:** [Identity](identity-concepts.md)
