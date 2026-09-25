#!/usr/bin/env bash
# Usage: bash deploy/scripts/helm-rollback.sh <snapshot-file written by helm-snapshot.sh>
set -euo pipefail
NS=${NS:-orderflow}
SNAPSHOT=${1:?usage: helm-rollback.sh <snapshot-file>}

while read -r release revision; do
  [[ -z "${release:-}" ]] && continue
  current=$(helm list -n "$NS" -f "^${release}\$" -o json | jq -r '.[0].revision // empty')
  if [[ "$current" == "$revision" ]]; then
    echo "=  $release already at revision $revision"
    continue
  fi
  echo "<- $release: revision $current -> $revision"
  helm rollback "$release" "$revision" -n "$NS" --wait --timeout 5m
done < "$SNAPSHOT"

kubectl get pods -n "$NS"
