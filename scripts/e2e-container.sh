#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export TESTINGPLATFORM_TELEMETRY_OPTOUT=1

port=5080
work=/work
app=/tmp/app

mkdir -p "$work"
tar -C /src \
    --exclude=./.git --exclude=./artifacts \
    --exclude='*/bin' --exclude='*/obj' --exclude='*/node_modules' \
    -cf - . | tar -C "$work" -xf -
cd "$work"

dotnet publish src/Chaos.Web --configuration Release --output "$app"

ASPNETCORE_URLS="http://127.0.0.1:$port" \
ASPNETCORE_ENVIRONMENT=Production \
Chaos__BoardCapacity=5 \
Chaos__PostBurst=1000 \
Chaos__PostsPerSecond=1000 \
Chaos__ResetBurst=1000 \
Chaos__ResetsPerSecond=1000 \
Chaos__PressBurst=1000 \
Chaos__PressesPerSecond=1000 \
Chaos__SpinMilliseconds=1500 \
Chaos__LatencyServerProbe=false \
Chaos__LatencyCooldownSeconds=1 \
Chaos__LatencyGapSeconds=0 \
Chaos__LatencyBudgetSeconds=5 \
Chaos__MaxConnectionsPerClient=50 \
Chaos__RequestBurst=100000 \
Chaos__RequestsPerSecond=100000 \
    dotnet "$app/Chaos.Web.dll" >/out/app.log 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT

ready=false
for _ in $(seq 1 60); do
    if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
        ready=true
        break
    fi
    sleep 1
done

if [ "$ready" != true ]; then
    printf 'error: the app did not start\n' >&2
    cat /out/app.log >&2
    exit 1
fi

status=0
CHAOS_BASE_URL="http://127.0.0.1:$port/" \
    dotnet test --project tests/Chaos.E2E --configuration Release --results-directory /out/results || status=$?

if [ "$status" -ne 0 ]; then
    printf '\n==> Last 50 lines of the app log\n' >&2
    tail -n 50 /out/app.log >&2
fi

exit "$status"
