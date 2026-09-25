#!/usr/bin/env bash
# Week 4: the identities GitHub Actions logs in as. Run from OrderFlow/:  bash infra/github-identities.sh
# Idempotent: re-running updates the federated credentials and skips role assignments that exist.
set -euo pipefail
source "$(dirname "$0")/env.sh"

ISSUER="https://token.actions.githubusercontent.com"
RG_ID=$(az group show -n "$RG" --query id -o tsv)
ACR_ID=$(az acr show -n "$ACR" --query id -o tsv)
AKS_ID=$(az aks show -g "$RG" -n "$AKS" --query id -o tsv)
APIM_ID=$(az apim show -g "$RG" -n "$APIM" --query id -o tsv 2>/dev/null || echo "")
SWA_ID=$(az staticwebapp show -g "$RG" -n "$SWA" --query id -o tsv 2>/dev/null || echo "")

# --assignee-principal-type skips the Graph lookup, which avoids "PrincipalNotFound" errors
# when an identity was created seconds ago and hasn't replicated yet.
assign() {  # $1 principalId  $2 role  $3 scope
  az role assignment create --assignee-object-id "$1" --assignee-principal-type ServicePrincipal \
    --role "$2" --scope "$3" -o none
  echo "   + $2"
}
fic() {     # $1 identity  $2 credential name  $3 subject
  az identity federated-credential create -g "$RG" --identity-name "$1" --name "$2" \
    --issuer "$ISSUER" --subject "$3" --audiences "api://AzureADTokenExchange" -o none
  echo "   + trusts $3"
}

echo "==> id-orderflow-gha-build   (build, scan, push; pull for the staging e2e)"
az identity create -g "$RG" -n id-orderflow-gha-build -l "$LOCATION" -o none
fic id-orderflow-gha-build gh-main    "repo:${GH_OIDC_REPO}:ref:refs/heads/main"
fic id-orderflow-gha-build gh-staging "repo:${GH_OIDC_REPO}:environment:staging"
BUILD_P=$(az identity show -g "$RG" -n id-orderflow-gha-build --query principalId -o tsv)
assign "$BUILD_P" AcrPush "$ACR_ID"
assign "$BUILD_P" Reader  "$ACR_ID"

echo "==> id-orderflow-gha-deploy  (production only)"
az identity create -g "$RG" -n id-orderflow-gha-deploy -l "$LOCATION" -o none
fic id-orderflow-gha-deploy gh-production "repo:${GH_OIDC_REPO}:environment:production"
DEPLOY_P=$(az identity show -g "$RG" -n id-orderflow-gha-deploy --query principalId -o tsv)
assign "$DEPLOY_P" Reader                                        "$RG_ID"
assign "$DEPLOY_P" "Azure Kubernetes Service Cluster User Role" "$AKS_ID"
assign "$DEPLOY_P" AcrPull                                       "$ACR_ID"
if [[ -n "$APIM_ID" ]]; then assign "$DEPLOY_P" "API Management Service Contributor" "$APIM_ID"; fi
if [[ -n "$SWA_ID"  ]]; then assign "$DEPLOY_P" Contributor                          "$SWA_ID"; fi

echo
echo "GitHub variables (1C):"
echo "  AZURE_TENANT_ID         $(az account show --query tenantId -o tsv)"
echo "  AZURE_SUBSCRIPTION_ID   $(az account show --query id -o tsv)"
echo "  AZURE_BUILD_CLIENT_ID   $(az identity show -g "$RG" -n id-orderflow-gha-build  --query clientId -o tsv)"
echo "  AZURE_DEPLOY_CLIENT_ID  $(az identity show -g "$RG" -n id-orderflow-gha-deploy --query clientId -o tsv)   (production environment)"
