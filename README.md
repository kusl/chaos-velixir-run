# chaos-velixir-run

A message board and a shared drawing canvas that everyone sees live and nobody keeps. Blazor Server on .NET 10, on the [velixir](https://velixir.net) free plan (0.25 vCPU, 256 MB). Live at https://chaos.velixir.run/.

- Board: the server keeps the newest 50 messages; each browser keeps up to 10,000 in IndexedDB and offers its newest 50 back after a server restart. Dotted border means the server no longer has it.
- Canvas: one black pen on a shared square page, live for everyone, one reset button, lost on restart.
- No third-party code, no cookies, no accounts. Strict CSP with no inline scripts.

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

Limits can be changed without a rebuild through environment variables, for example `Chaos__MaxConnections=60`. See `src/Chaos.Web/Infrastructure/ChaosOptions.cs`.

## License

AGPL-3.0-or-later. See `LICENSE` and `REUSE.toml`.
