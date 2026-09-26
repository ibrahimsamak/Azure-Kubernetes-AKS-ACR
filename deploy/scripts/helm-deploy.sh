#!/usr/bin/env bash
set -euo pipefail

TARGET=${1:-local}
TAG=${TAG:-local}
NS=orderflow
CHART=deploy/helm/orderflow-service
VALUES=deploy/helm/values

SERVICES=(inventory payment notification order gateway)

for svc in "${SERVICES[@]}"; do
  args=(
    upgrade --install "$svc" "$CHART"
    --namespace "$NS"
    -f "$VALUES/common.yaml"
  )
  if [[ "$TARGET" == "local" ]]; then
    args+=(--create-namespace)
  fi

  if [[ "$TARGET" == "azure" ]]; then
    : "${RG:?source infra/env.sh first}"
    args+=(-f "$VALUES/azure.generated.yaml")

    client_id=$(az identity show -g "$RG" -n "id-orderflow-$svc" --query clientId -o tsv)
    args+=(--set workloadIdentity.enabled=true --set "workloadIdentity.clientId=$client_id")
    if [[ "$svc" == "payment" ]]; then
      args+=(--set "env.KeyVault__Uri=https://${KV}.vault.azure.net/")
    fi
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
