# 0019. AKS: Entra ID + Azure RBAC for Kubernetes, local accounts disabled

## Status
Accepted — Week 4, Day 3.

## Context
The cluster was created with local accounts: `az aks get-credentials` hands out a client
certificate that is effectively cluster-admin, never expires on its own, and can't be tied to a
person or revoked individually. The pipeline's deploy identity had the same power.

## Decision
- Enable **Entra ID integration** with **Azure RBAC for Kubernetes authorization**; **disable
  local accounts**. kubectl authenticates through `kubelogin` with an Entra token.
- Humans get `Azure Kubernetes Service RBAC Cluster Admin` (this is a one-person project); the
  deploy identity gets `Azure Kubernetes Service RBAC Writer` scoped to the **`orderflow`
  namespace only** — enough for Helm (Deployments, Services, ConfigMaps, HPAs, PDBs, Ingress,
  NetworkPolicies, Helm's release Secrets), nothing cluster-wide.

## Alternatives considered
- **Entra ID + Kubernetes RBAC (RoleBindings to Entra groups)** — equally valid; Azure RBAC keeps
  every grant in one place (IAM blade, `az role assignment list`) and in our scripts.
- **Keep local accounts, rotate certificates** — rotation is manual and all-or-nothing.

## Consequences
- The pipeline can't create namespaces or cluster-scoped objects; Helm runs without
  `--create-namespace` in Azure.
- `kubectl` on a new machine needs `kubelogin convert-kubeconfig -l azurecli` once.
- Emergency access if Entra is unavailable is gone; `az aks update --enable-local-accounts`
  (an audited ARM operation) is the break-glass procedure.
