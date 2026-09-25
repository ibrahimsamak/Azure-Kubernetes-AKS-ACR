#!/usr/bin/env bash
# Usage: source infra/env.sh      (Git Bash or Azure Cloud Shell, from OrderFlow/)

# Git Bash on Windows rewrites "/subscriptions/..." into "C:/Program Files/Git/subscriptions/...".
# This stops that. Harmless everywhere else.
export MSYS_NO_PATHCONV=1

# The Windows az CLI prints CRLF (also when called from WSL), so $(az ... -o tsv) captures a
# trailing "\r" that breaks IDs and scopes. Strip it; harmless with a native Linux az.
# With pipefail set, az's exit status still propagates.
az() { command az "$@" | tr -d '\r'; }

export SUFFIX="${SUFFIX:-ibs01}"            # CHANGE ME — lowercase letters/digits, 3-6 chars
export LOCATION="${LOCATION:-eastus}" # pick the region closest to you that has quota

export RG="rg-orderflow-dev"
export AKS="aks-orderflow-dev"
export ACR="acrorderflow${SUFFIX}"          # letters+digits only, globally unique
export SQL_SERVER="sql-orderflow-${SUFFIX}"
export EH_NS="evhns-orderflow-${SUFFIX}"
export SB_NS="sbns-orderflow-${SUFFIX}"
export REDIS="redis-orderflow-${SUFFIX}"
export KV="kv-orderflow-${SUFFIX}"          # max 24 chars
export APIM="apim-orderflow-${SUFFIX}"
export FUNC="func-orderflow-${SUFFIX}"
export FUNC_STORAGE="stfunc${SUFFIX}"       # letters+digits only, max 24
export SWA="swa-orderflow-${SUFFIX}"
export SWA_LOCATION="centralus"             # Static Web Apps is only offered in a few regions

export K8S_NS="orderflow"
export SERVICES="order inventory payment notification"
export DATABASES="orderflow-orders orderflow-inventory orderflow-payments orderflow-notifications"
export TOPICS="orderflow.orders.v1 orderflow.inventory.v1 orderflow.payments.v1"

# ---- Week 4 ----
export GH_REPO="${GH_REPO:-ibrahimsamak/Azure-Kubernetes-AKS-ACR}"   # owner/repo, for gh commands
# The OIDC `sub` GitHub issues for this repo uses the ID-qualified form owner@<id>/repo@<id>.
# Entra compares it byte for byte, so the federated credentials must use exactly this.
export GH_OIDC_REPO="${GH_OIDC_REPO:-ibrahimsamak@12866385/Azure-Kubernetes-AKS-ACR@1379554158}"

export LAW="log-orderflow-${SUFFIX}"
export APPI="appi-orderflow-${SUFFIX}"            # backend: Entra-only ingestion
export APPI_WEB="appi-orderflow-web-${SUFFIX}"    # browser: can't hold an Entra token (2P)
export ACTION_GROUP="ag-orderflow-oncall"
