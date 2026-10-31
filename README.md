# chaos-velixir-run

A message board, a shared drawing canvas, a shared slot machine and a shared latency test that everyone sees live and nobody keeps. Blazor Server on .NET 10, on the [velixir](https://velixir.net) free plan (0.25 vCPU, 256 MB). Live at https://chaos.velixir.run/.

- Board: the server keeps the newest 50 messages; each browser keeps up to 10,000 in IndexedDB and offers its newest 50 back after a server restart. Dotted border means the server no longer has it. Your own posts show `Your message` (this browser only). Optional: after the poster opts in and grants geolocation, each post carries a collapsed `Details` list with the browser's location, time zone and device details. No lookups, no third parties, no IP stored.
- Canvas: one black pen on a shared square page, live for everyone, one reset button. Browsers keep finished strokes (10,000) and the latest reset, and offer back up to 500 strokes after a server restart.
- Slots: one shared machine. A press draws five symbols (A–Z, 0–9) on the server and locks the button for everyone until the reels stop. Three of a kind or a five-symbol straight wins. Four or five of a kind, or an unbroken sequence along `0-9A-Z` (`ABCDE`, `43210`), is a jackpot: everyone watching sees a celebration, and jackpots are never deleted, on the server or in the browser. Results are kept like board messages, plus the newest 500 winners. Tapping a winner shows its odds and the probability behind them. Ten successful presses unlock auto spin, which also runs in background tabs.
- Latency: one button times public endpoints from the browser and from the server. One test at a time, one per network every 5 minutes. Results are kept like board messages, without identity. Below them: the 10 fastest and slowest browser and server medians, then jackpots (a median under 10 ms wins, 100 ms or more loses). Browsers offer back their own 10 best and worst of each after a restart. Ten tests unlock auto run.
- Every page footer shows the ticking server time.
- No third-party code, no cookies, no accounts. Strict CSP with no inline scripts; `connect-src` also lists the latency endpoints.

## Requirements

- .NET SDK 10.0.100 or later in the 10.0 band
- Node.js 22 or later (JavaScript tests)
- Podman 4 or later (end-to-end tests)

## Commands

```
dotnet run --project src/Chaos.Web
scripts/run-all.sh [--skip-e2e] [--skip-js] [--skip-export]
```

`run-all.sh` builds with warnings as errors, runs the .NET and JavaScript tests with coverage, runs Playwright in Podman, then writes `docs/llm/dump.txt`. Output lands in `artifacts/`. Exit code 2 is a usage error, 127 a missing tool.

## Layout

| Path | What |
| --- | --- |
| `src/Chaos.Web` | The app. The only folder velixir builds. |
| `tests/Chaos.Web.Tests` | xUnit v3 unit and integration tests |
| `tests/Chaos.E2E` | Playwright tests, run in Podman |
| `tests/js` | Vitest tests for the hand-written modules |
| `scripts` | Build, test and export scripts |
| `docs/adr` | Architecture decision records |

## Deploy

Every push to `main` runs `.github/workflows/velixir.yml`, which uploads `src/Chaos.Web` only. It needs the `VELIXIR_API_KEY` repository secret. Every push also runs `.github/workflows/ci.yml`. Turn on the HTTP health probe (`/health`) under Scaling, Runtime in velixir.

Limits can be changed without a rebuild through environment variables, for example `Chaos__MaxConnections=60`. See `src/Chaos.Web/Infrastructure/ChaosOptions.cs`. `Chaos__SlotDrawSymbols=7` makes every spin a jackpot, for local testing only.

## License

AGPL-3.0-or-later. See `LICENSE` and `REUSE.toml`.
