#!/usr/bin/env bash
# Week 4 Day 3 (3K): AKS authenticates only Entra ID identities; Azure RBAC authorizes them.
# Run from OrderFlow/:  bash infra/aks-entra-rbac.sh      (idempotent; ADR-0019)
#
# ORDER MATTERS: give yourself the admin role first, then switch the cluster, then remove local
# accounts — otherwise you lock yourself out (recoverable: az aks update --enable-local-accounts).
set -euo pipefail
source "$(dirname "$0")/env.sh"

AKS_ID=$(az aks show -g "$RG" -n "$AKS" --query id -o tsv)
ME=$(az ad signed-in-user show --query id -o tsv)
DEPLOY_P=$(az identity show -g "$RG" -n id-orderflow-gha-deploy --query principalId -o tsv)

echo "==> 1) you: Cluster Admin (one-person project; on a team: a group, and Cluster Admin for break-glass only)"
az role assignment create --assignee-object-id "$ME" --assignee-principal-type User \
  --role "Azure Kubernetes Service RBAC Cluster Admin" --scope "$AKS_ID" -o none

echo "==> 2) Entra ID authentication + Azure RBAC for Kubernetes authorization"
az aks update -g "$RG" -n "$AKS" --enable-aad --enable-azure-rbac -o none

echo "==> 3) the pipeline: RBAC Writer in the $K8S_NS namespace only (also in infra/github-identities.sh)"
az role assignment create --assignee-object-id "$DEPLOY_P" --assignee-principal-type ServicePrincipal \
  --role "Azure Kubernetes Service RBAC Writer" --scope "$AKS_ID/namespaces/$K8S_NS" -o none

echo "==> 4) kubectl through Entra via kubelogin"
az aks get-credentials -g "$RG" -n "$AKS" --overwrite-existing
kubelogin convert-kubeconfig -l azurecli
# Helm in the pipeline can't create namespaces any more; make sure this one exists (as you).
kubectl get namespace "$K8S_NS" >/dev/null 2>&1 || kubectl create namespace "$K8S_NS"
kubectl get pods -n "$K8S_NS"

echo "==> 5) only now: no more certificates"
az aks update -g "$RG" -n "$AKS" --disable-local-accounts -o none

echo
echo "Check: 'az aks get-credentials -g $RG -n $AKS --admin' must now FAIL."
