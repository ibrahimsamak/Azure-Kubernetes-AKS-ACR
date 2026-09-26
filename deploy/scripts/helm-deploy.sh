#!/usr/bin/env bash
# Usage (from OrderFlow/):
#   deploy/scripts/helm-deploy.sh local
#   TAG=20260914-1 deploy/scripts/helm-deploy.sh azure
set -euo pipefail

TARGET=${1:-local}
TAG=${TAG:-local}
NS=orderflow
CHART=deploy/helm/orderflow-service
VALUES=deploy/helm/values

# Order matters: inventory must be ready before order (gRPC), order before gateway.
SERVICES=(inventory payment notification order gateway)

for svc in "${SERVICES[@]}"; do
  args=(
    upgrade --install "$svc" "$CHART"
    --namespace "$NS"
    -f "$VALUES/common.yaml"
  )
  # In Azure the deploy identity is scoped to ONE namespace and can't read Namespace objects, which
  # --create-namespace needs. The namespace was created once by an admin (3K).
  if [[ "$TARGET" == "local" ]]; then
    args+=(--create-namespace)
  fi

  if [[ "$TARGET" == "azure" ]]; then
    : "${RG:?source infra/env.sh first}"
    args+=(-f "$VALUES/azure.generated.yaml")
    # # if [[ "$svc" != "gateway" ]]; then
    #   client_id=$(az identity show -g "$RG" -n "id-orderflow-$svc" --query clientId -o tsv)
    #   args+=(--set workloadIdentity.enabled=true --set "workloadIdentity.clientId=$client_id")

    # Every release has a managed identity now; the gateway's only role is sending telemetry.
    client_id=$(az identity show -g "$RG" -n "id-orderflow-$svc" --query clientId -o tsv)
    args+=(--set workloadIdentity.enabled=true --set "workloadIdentity.clientId=$client_id")
    # fi
    # Only payment has a Key Vault role. Giving the URI to the others would make their
    # startup fail with 403 — least privilege shows up in config too.
    if [[ "$svc" == "payment" ]]; then
      args+=(--set "env.KeyVault__Uri=https://${KV}.vault.azure.net/")
    fi
    # Inventory validates tokens for ITS OWN audience, not orderflow-api's.
    if [[ "$svc" == "inventory" ]]; then
      args+=(--set "env.AzureAd__ClientId=$INVENTORY_APP_ID")
    fi
  else
    args+=(-f "$VALUES/local.yaml")
  fi

  args+=(-f "$VALUES/$svc.yaml" --set "image.tag=$TAG" --wait --timeout 6m)

  echo "==> helm ${args[*]}"
  helm "${args[@]}"
done

kubectl get pods,svc,hpa,ingress -n "$NS"
