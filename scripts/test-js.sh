#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
require node npm
cd "$ROOT/tests/js"

step "JavaScript unit tests"
npm ci --no-audit --no-fund
npx vitest run --coverage
