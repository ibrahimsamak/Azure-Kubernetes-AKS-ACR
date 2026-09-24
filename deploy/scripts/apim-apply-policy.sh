#!/usr/bin/env bash
# Applies deploy/apim/orderflow-api-policy.xml to the "orderflow" API. Idempotent. Run from OrderFlow/.
set -euo pipefail
source infra/env.sh

SUB=$(az account show --query id -o tsv)
URI="https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/apis/orderflow/policies/policy?api-version=2022-08-01"
BODY="${RUNNER_TEMP:-/tmp}/apim-policy.json"

# rawxml = the policy exactly as written (no XML escaping of the @(...) expressions).
jq -n --rawfile xml deploy/apim/orderflow-api-policy.xml '{properties: {format: "rawxml", value: $xml}}' > "$BODY"
az rest --method put --uri "$URI" --body "@$BODY" -o none
echo "APIM policy applied to $APIM/orderflow"
