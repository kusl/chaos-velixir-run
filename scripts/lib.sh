ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export TESTINGPLATFORM_TELEMETRY_OPTOUT=1

step() {
    printf '\n==> %s\n' "$*"
}

require() {
    local tool
    for tool in "$@"; do
        if ! command -v "$tool" >/dev/null 2>&1; then
            printf 'error: %s is required but was not found on PATH\n' "$tool" >&2
            exit 127
        fi
    done
}

playwright_version() {
    local version
    version="$(sed -n 's/.*Include="Microsoft.Playwright" Version="\([^"]*\)".*/\1/p' "$ROOT/Directory.Packages.props")"
    if [ -z "$version" ]; then
        printf 'error: no Microsoft.Playwright version in Directory.Packages.props\n' >&2
        exit 1
    fi
    printf '%s\n' "$version"
}

coverage_percent() {
    local rate
    rate="$(sed -n 's/.*<coverage[^>]* line-rate="\([0-9.]*\)".*/\1/p' "$1" | head -n 1)"
    if [ -n "$rate" ]; then
        awk -v rate="$rate" 'BEGIN { printf "%.1f%%\n", rate * 100 }'
    else
        printf 'unknown\n'
    fi
}
