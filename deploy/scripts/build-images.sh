#!/usr/bin/env bash
# Usage (from OrderFlow/):
#   deploy/scripts/build-images.sh local            -> docker build, tag :local
#   TAG=20260914-1 deploy/scripts/build-images.sh acr  -> az acr build (runs IN Azure, no local Docker needed)
set -euo pipefail

MODE=${1:-local}
TAG=${TAG:-local}

# Portable list (works in macOS's bash 3.2, which has no associative arrays).
# Each entry is "image-name|path-to-Dockerfile".
DOCKERFILES=(
  "order-api|src/Services/Order/OrderFlow.Order.Api/Dockerfile"
  "inventory-api|src/Services/Inventory/OrderFlow.Inventory.Api/Dockerfile"
  "payment-api|src/Services/Payment/OrderFlow.Payment.Api/Dockerfile"
  "notification-api|src/Services/Notification/OrderFlow.Notification.Api/Dockerfile"
  "gateway|src/Gateway/OrderFlow.Gateway/Dockerfile"
)

for entry in "${DOCKERFILES[@]}"; do
  image=${entry%%|*}   # text before the "|"
  file=${entry#*|}     # text after the "|"
  echo "==> $image ($file) tag=$TAG mode=$MODE"
  if [[ "$MODE" == "acr" ]]; then
    : "${ACR:?source infra/env.sh first}"
    az acr build --registry "$ACR" --image "orderflow/$image:$TAG" --file "$file" . --no-logs
  else
    docker build --file "$file" --tag "orderflow/$image:$TAG" .
  fi
done
