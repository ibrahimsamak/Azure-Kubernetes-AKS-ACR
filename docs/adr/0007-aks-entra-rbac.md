# 0007. AKS with Entra ID and Azure RBAC

**Status:** accepted

## Context

The cluster was created with local accounts. `az aks get-credentials` gives you a client cert that
is basically cluster admin, doesn't expire and can't be revoked per person. The pipeline identity
had the same access.

## Decision

- Turn on Entra ID integration with Azure RBAC for Kubernetes, and disable local accounts. kubectl
  logs in through `kubelogin`.
- My user gets `Azure Kubernetes Service RBAC Cluster Admin`. The deploy identity gets
  `Azure Kubernetes Service RBAC Writer` on the `orderflow` namespace only, which is enough for
  Helm and nothing cluster wide.

## Alternatives

- Entra ID with Kubernetes RBAC (RoleBindings to Entra groups): also fine, but with Azure RBAC all
  the grants are in one place and in the scripts.
- Keep local accounts and rotate certs: manual, and all or nothing.

## Consequences

- The pipeline can't create namespaces, so Helm runs without `--create-namespace` on Azure.
- A new machine needs `kubelogin convert-kubeconfig -l azurecli` once.
- If Entra is down there's no way in. The fallback is `az aks update --enable-local-accounts`.
