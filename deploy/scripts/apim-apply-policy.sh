#!/usr/bin/env bash
set -euo pipefail
source infra/env.sh

SUB=$(az account show --query id -o tsv)
URI="https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/apis/orderflow/policies/policy?api-version=2022-08-01"
BODY="${RUNNER_TEMP:-/tmp}/apim-policy.json"

jq -n --rawfile xml deploy/apim/orderflow-api-policy.xml '{properties: {format: "rawxml", value: $xml}}' > "$BODY"
az rest --method put --uri "$URI" --body "@$BODY" -o none
echo "APIM policy applied to $APIM/orderflow"
