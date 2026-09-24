#!/usr/bin/env bash
# Builds and deploys web/orderflow-web to Azure Static Web Apps. Run from OrderFlow/.
set -euo pipefail
source infra/env.sh

token=$(az staticwebapp secrets list -g "$RG" -n "$SWA" --query properties.apiKey -o tsv)
# In GitHub Actions: never print it, even by accident.
if [[ -n "${GITHUB_ACTIONS:-}" ]]; then echo "::add-mask::$token"; fi

cd web/orderflow-web
npm ci
npx ng build --configuration production
npx --yes @azure/static-web-apps-cli deploy ./dist/orderflow-web/browser \
  --deployment-token "$token" --env production
