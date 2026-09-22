#!/usr/bin/env bash
set -euo pipefail

MODE=${1:-local}
TAG=${TAG:-local}

DOCKERFILES=(
  "order-api|src/Services/Order/OrderFlow.Order.Api/Dockerfile"
  "inventory-api|src/Services/Inventory/OrderFlow.Inventory.Api/Dockerfile"
  "payment-api|src/Services/Payment/OrderFlow.Payment.Api/Dockerfile"
  "notification-api|src/Services/Notification/OrderFlow.Notification.Api/Dockerfile"
  "gateway|src/Gateway/OrderFlow.Gateway/Dockerfile"
)

for entry in "${DOCKERFILES[@]}"; do
  image=${entry%%|*}
  file=${entry#*|}
  echo "==> $image ($file) tag=$TAG mode=$MODE"
  if [[ "$MODE" == "acr" ]]; then
    : "${ACR:?source infra/env.sh first}"
    az acr build --registry "$ACR" --image "orderflow/$image:$TAG" --file "$file" . --no-logs
  else
    docker build --file "$file" --tag "orderflow/$image:$TAG" .
  fi
done
