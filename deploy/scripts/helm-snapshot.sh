#!/usr/bin/env bash
set -euo pipefail
NS=${NS:-orderflow}
helm list -n "$NS" --deployed -o json | jq -r '.[] | "\(.name) \(.revision)"'
