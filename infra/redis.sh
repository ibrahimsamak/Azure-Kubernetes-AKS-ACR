#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"

az extension add --name redisenterprise --upgrade --yes

az redisenterprise create -g "$RG" -n "$REDIS" -l "$LOCATION" \
  --sku Balanced_B0 \
  --public-network-access Enabled

# Grant the order identity data access (Entra auth). If this fails on your CLI version,
# use Portal: Azure Managed Redis -> Settings -> Authentication -> Microsoft Entra Authentication -> Add.
ORDER_PRINCIPAL=$(az identity show -g "$RG" -n id-orderflow-order --query principalId -o tsv)
az redisenterprise database access-policy-assignment create -g "$RG" \
  --cluster-name "$REDIS" --database-name default \
  --access-policy-assignment-name order \
  --access-policy-name default \
  --object-id "$ORDER_PRINCIPAL"

az redisenterprise show -g "$RG" -n "$REDIS" --query hostName -o tsv