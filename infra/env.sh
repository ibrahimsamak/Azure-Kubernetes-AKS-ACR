#!/usr/bin/env bash

export MSYS_NO_PATHCONV=1

az() { command az "$@" | tr -d '\r'; }

export SUFFIX="${SUFFIX:-ibs01}"
export LOCATION="${LOCATION:-eastus}"

export RG="rg-orderflow-dev"
export AKS="aks-orderflow-dev"
export ACR="acrorderflow${SUFFIX}"
export SQL_SERVER="sql-orderflow-${SUFFIX}"
export EH_NS="evhns-orderflow-${SUFFIX}"
export SB_NS="sbns-orderflow-${SUFFIX}"
export REDIS="redis-orderflow-${SUFFIX}"
export KV="kv-orderflow-${SUFFIX}"
export APIM="apim-orderflow-${SUFFIX}"
export FUNC="func-orderflow-${SUFFIX}"
export FUNC_STORAGE="stfunc${SUFFIX}"
export SWA="swa-orderflow-${SUFFIX}"
export SWA_LOCATION="centralus"

export K8S_NS="orderflow"
export SERVICES="order inventory payment notification"
export DATABASES="orderflow-orders orderflow-inventory orderflow-payments orderflow-notifications"
export TOPICS="orderflow.orders.v1 orderflow.inventory.v1 orderflow.payments.v1"

export GH_REPO="${GH_REPO:-ibrahimsamak/Azure-Kubernetes-AKS-ACR}"
export GH_OIDC_REPO="${GH_OIDC_REPO:-ibrahimsamak@12866385/Azure-Kubernetes-AKS-ACR@1379554158}"

export LAW="log-orderflow-${SUFFIX}"
export APPI="appi-orderflow-${SUFFIX}"
export APPI_WEB="appi-orderflow-web-${SUFFIX}"
export ACTION_GROUP="ag-orderflow-oncall"

export TENANT_ID="${TENANT_ID:-345f3725-4a76-4514-b265-f36d116154cc}"
export API_APP_ID="${API_APP_ID:-28b01914-9a4a-4555-8626-d516a358da1b}"
export SPA_APP_ID="${SPA_APP_ID:-74acbc9d-f79e-4499-b669-19392d3dfdde}"
export INVENTORY_APP_ID="${INVENTORY_APP_ID:-}"
