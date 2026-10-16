#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export TESTINGPLATFORM_TELEMETRY_OPTOUT=1

port=5080
rigged=5081
work=/work
app=/tmp/app

mkdir -p "$work"
tar -C /src \
    --exclude=./.git --exclude=./artifacts \
    --exclude='*/bin' --exclude='*/obj' --exclude='*/node_modules' \
    -cf - . | tar -C "$work" -xf -
cd "$work"

dotnet publish src/Chaos.Web --configuration Release --output "$app"

start() {
    env \
        ASPNETCORE_URLS="http://127.0.0.1:$1" \
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
        "${@:3}" \
        dotnet "$app/Chaos.Web.dll" >"/out/$2" 2>&1 &
}

wait_for() {
    for _ in $(seq 1 60); do
        if (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; then
            return 0
        fi
        sleep 1
    done
    printf 'error: the app on port %s did not start\n' "$1" >&2
    cat "/out/$2" >&2
    return 1
}

start "$port" app.log
server=$!
start "$rigged" rigged.log Chaos__SlotDrawSymbols=7
rigged_server=$!
trap 'kill "$server" "$rigged_server" 2>/dev/null || true' EXIT

wait_for "$port" app.log
wait_for "$rigged" rigged.log

status=0
CHAOS_BASE_URL="http://127.0.0.1:$port/" \
CHAOS_RIGGED_URL="http://127.0.0.1:$rigged/" \
    dotnet test --project tests/Chaos.E2E --configuration Release --results-directory /out/results || status=$?

if [ "$status" -ne 0 ]; then
    printf '\n==> Last 50 lines of the app log\n' >&2
    tail -n 50 /out/app.log >&2
    printf '\n==> Last 50 lines of the rigged app log\n' >&2
    tail -n 50 /out/rigged.log >&2
fi

exit "$status"
