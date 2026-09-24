#!/usr/bin/env bash
# Prints "<release> <revision>" for every deployed Helm release in the namespace — the rollback target.
set -euo pipefail
NS=${NS:-orderflow}
helm list -n "$NS" --deployed -o json | jq -r '.[] | "\(.name) \(.revision)"'
