#!/usr/bin/env bash
set -euo pipefail

# Safe policy:
# - prune only Docker build cache older than 7 days
# - prune dangling images older than 7 days
# - never prune volumes or running images/containers

if docker buildx version >/dev/null 2>&1; then
  docker buildx prune --all --force --filter "until=168h"
else
  docker builder prune -af --filter "until=168h"
fi

docker image prune -f --filter "until=168h"
