#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
    printf 'usage: %s [--skip-e2e] [--skip-js] [--skip-export]\n' "${0##*/}"
}

skip_e2e=false
skip_js=false
skip_export=false

for argument in "$@"; do
    case "$argument" in
        --skip-e2e) skip_e2e=true ;;
        --skip-js) skip_js=true ;;
        --skip-export) skip_export=true ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done

"$ROOT/scripts/build.sh"
"$ROOT/scripts/test-unit.sh"
if [ "$skip_js" = false ]; then
    "$ROOT/scripts/test-js.sh"
fi
if [ "$skip_e2e" = false ]; then
    "$ROOT/scripts/test-e2e.sh"
fi
if [ "$skip_export" = false ]; then
    "$ROOT/scripts/export.sh"
fi

step "All steps passed"
