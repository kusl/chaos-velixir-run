#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
require dotnet
cd "$ROOT"

results="$ARTIFACTS/test-results/unit"
rm -rf "$results" "$ARTIFACTS/coverage/dotnet"
mkdir -p "$ARTIFACTS/coverage/dotnet"

step "Unit and integration tests"
dotnet test --project tests/Chaos.Web.Tests --configuration Release --no-build \
    --results-directory "$results" \
    --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml

report="$(find "$results" -name coverage.cobertura.xml -print -quit)"
if [ -n "$report" ]; then
    cp "$report" "$ARTIFACTS/coverage/dotnet/coverage.cobertura.xml"
    printf '.NET line coverage: %s\n' "$(coverage_percent "$report")"
fi
