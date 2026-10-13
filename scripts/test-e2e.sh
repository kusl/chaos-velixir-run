#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
require podman
cd "$ROOT"

version="$(playwright_version)"
image="mcr.microsoft.com/playwright/dotnet:v$version-noble"
out="$ARTIFACTS/e2e"
rm -rf "$out"
mkdir -p "$out"

step "End-to-end tests in $image"
podman pull --quiet "$image" >/dev/null
podman run --rm \
    --shm-size=1g \
    --security-opt label=disable \
    --volume "$ROOT:/src:ro" \
    --volume "$out:/out" \
    --volume chaos-e2e-nuget:/root/.nuget/packages \
    --env CI="${CI:-}" \
    "$image" bash /src/scripts/e2e-container.sh
