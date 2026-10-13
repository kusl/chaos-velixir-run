# 0017 Scripts and CI

Status: accepted

- All logic is in `scripts/`. Each script uses `set -euo pipefail`, can be rerun, and cleans its own output in `artifacts/`.
- `run-all.sh` runs build, unit, JavaScript, end-to-end and export in that order, with `--skip-e2e`, `--skip-js` and `--skip-export`. Exit codes: 0 success, 2 usage, 127 missing tool, otherwise the failing step's code.
- Podman runs with `--security-opt label=disable` so the repository is not relabelled on SELinux hosts. The repository is mounted read-only and copied inside the container; NuGet packages are cached in a named volume.
- `ci.yml` installs .NET and Node, runs `run-all.sh` on every push, and uploads `artifacts/`. `velixir.yml` deploys on every push to `main`.
